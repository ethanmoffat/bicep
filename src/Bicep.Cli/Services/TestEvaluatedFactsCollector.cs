// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Semantics;
using Bicep.Core.TestFramework;
using Bicep.Core.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Works out what a target would actually deploy for one input case, offline.
///
/// Every branch is computed on demand and only once. A policy that asks about source declarations never
/// causes an evaluation, and a policy that asks only about the selected file is never held up - or
/// failed - by a module it did not ask about.
/// </summary>
public sealed class TestEvaluatedFactsProvider(
    SemanticModel targetModel,
    Func<JObject?> parameters,
    TestDeploymentContext? deploymentContext,
    TestTargetFacts sourceFacts,
    string targetFile,
    TestMockRegistry? mocks = null)
{
    private const string DeploymentResourceType = "Microsoft.Resources/deployments";
    private const string DeploymentParametersSchema = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#";

    /// <summary>
    /// Each round resolves one more module-to-module link, so a chain longer than this is not resolved
    /// rather than retried forever. A bound is needed because a template whose values never settle would
    /// otherwise never finish.
    /// </summary>
    private const int MaxResolutionRounds = 8;

    /// <summary>
    /// One evaluated deployment: the template as this case computes it, plus the module calls it makes.
    /// Each module call is kept separately, because two calls to the same file with different arguments
    /// deploy different things.
    /// </summary>
    private sealed record EvaluatedDeployment(
        JObject Template,
        JToken Source,
        JObject? Inputs,
        string File,
        TestDeploymentContext Context,
        ImmutableDictionary<string, EvaluatedDeployment> Modules);

    private EvaluatedDeployment? root;
    private ImmutableArray<TestEvaluatedResource>? local;
    private ImmutableArray<TestEvaluatedResource>? withModules;
    private JObject? outputs;

    public ImmutableArray<TestEvaluatedResource> Local => local ??= Collect(Root(), string.Empty, recurse: false);

    public ImmutableArray<TestEvaluatedResource> WithModules => withModules ??= Collect(Root(), string.Empty, recurse: true);

    /// <summary>
    /// Outputs are computed strictly, and only when something asks for them. An output is a consumption:
    /// a value it needs and cannot get is an error about that value, not a silently unevaluated string
    /// that a later comparison would report as an ordinary mismatch.
    /// </summary>
    public JObject Outputs => outputs ??= CollectOutputs(EvaluateStrictly(Root()));

    private EvaluatedDeployment Root() => root ??= Evaluate(
        TestTemplateEmitter.Emit(targetModel, forceSymbolicNames: true),
        parameters(),
        deploymentContext ?? TestDeploymentContext.Empty,
        targetFile);

    /// <summary>
    /// Evaluates one template and everything it deploys.
    ///
    /// A module's arguments are only known once the calling template has been evaluated, and a caller's
    /// own values may depend on outputs of modules it called - including the arguments it passes to the
    /// next module. Each round resolves one more link of that chain, so the work repeats until the
    /// module outputs stop changing.
    /// </summary>
    private EvaluatedDeployment Evaluate(JToken template, JObject? inputs, TestDeploymentContext context, string file)
    {
        var modules = ImmutableDictionary.Create<string, EvaluatedDeployment>(StringComparer.OrdinalIgnoreCase);
        var evaluated = EvaluateTemplate(template, inputs, context, mocks, null, strictOutputs: false, tolerant: true);

        for (var round = 0; round < MaxResolutionRounds; round++)
        {
            var next = EvaluateModules(evaluated, context, file);
            var settled = Fingerprint(next) == Fingerprint(modules);

            modules = next;

            if (settled || modules.IsEmpty)
            {
                break;
            }

            evaluated = EvaluateTemplate(template, inputs, context, mocks, ModuleOutputResolver(modules), strictOutputs: false, tolerant: true);
        }

        // Everything the modules contribute is known by now, so nothing needs to be tolerated: a value
        // that still cannot be computed is a real failure and is reported as one.
        var authoritative = EvaluateTemplate(template, inputs, context, mocks, ModuleOutputResolver(modules), strictOutputs: false, tolerant: false);

        return new EvaluatedDeployment(authoritative, template, inputs, file, context, modules);
    }

    /// <summary>
    /// Identifies what the modules of a deployment currently contribute back to their caller. A round
    /// that produces the same contribution as the last one has nothing left to resolve.
    /// </summary>
    private static string Fingerprint(ImmutableDictionary<string, EvaluatedDeployment> modules)
        => string.Join(
            '\u0000',
            modules
                .OrderBy(module => module.Key, StringComparer.Ordinal)
                .Select(module => $"{module.Key}={module.Value.Template[TestTargetType.OutputsPropertyName]?.ToString(Formatting.None) ?? string.Empty}"));

    /// <summary>
    /// Re-evaluates a deployment with strict outputs, resolving module outputs from strict module
    /// evaluations so a failure is reported where it happens rather than propagated as text.
    /// </summary>
    private JObject EvaluateStrictly(EvaluatedDeployment deployment)
    {
        var modules = deployment.Modules.ToImmutableDictionary(
            module => module.Key,
            module => module.Value with { Template = EvaluateStrictly(module.Value) },
            StringComparer.OrdinalIgnoreCase);

        return EvaluateTemplate(
            deployment.Source,
            deployment.Inputs,
            deployment.Context,
            mocks,
            ModuleOutputResolver(modules),
            strictOutputs: true,
            tolerant: false);
    }

    private static TemplateEvaluator.OnUnresolvedReferenceDelegate? ModuleOutputResolver(ImmutableDictionary<string, EvaluatedDeployment>? modules)
    {
        if (modules is null || modules.IsEmpty)
        {
            return null;
        }

        var moduleOutputs = modules.ToDictionary(
            module => module.Key,
            module => (JToken)new JObject { [TestTargetType.OutputsPropertyName] = module.Value.Template[TestTargetType.OutputsPropertyName] ?? new JObject() },
            StringComparer.OrdinalIgnoreCase);

        return (reference, _, _) => moduleOutputs.TryGetValue(reference, out var resolved) ? resolved : null;
    }

    private static JObject EvaluateTemplate(
        JToken template,
        JObject? inputs,
        TestDeploymentContext context,
        TestMockRegistry? mocks,
        TemplateEvaluator.OnUnresolvedReferenceDelegate? onUnresolvedReference,
        bool strictOutputs,
        bool tolerant)
        => (JObject)TemplateEvaluator.Evaluate(
            template,
            inputs,
            configuration => TestMockRegistryExtensions.Apply(mocks, context.Apply(configuration)) with
            {
                OnUnresolvedReferenceFunc = onUnresolvedReference,
                StrictOutputs = strictOutputs,
                TolerateUnresolvedValues = tolerant,
            }).ToJToken();

    /// <summary>
    /// Evaluates each module call this template makes, keyed by the symbolic name the caller used so
    /// the caller can read the outputs back.
    /// </summary>
    private ImmutableDictionary<string, EvaluatedDeployment> EvaluateModules(JObject template, TestDeploymentContext context, string file)
    {
        var modules = ImmutableDictionary.CreateBuilder<string, EvaluatedDeployment>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, deployment) in Deployments(template))
        {
            if (TryGetModuleFile(file, BaseSymbolicName(key)) is not { } moduleFile)
            {
                continue;
            }

            if (deployment["properties"]?["template"] is not JObject nestedTemplate)
            {
                throw new InvalidOperationException($"The module deployed by '{key}' has no inline template to evaluate.");
            }

            var nestedInputs = new JObject
            {
                ["$schema"] = DeploymentParametersSchema,
                ["contentVersion"] = "1.0.0.0",
                ["parameters"] = deployment["properties"]?["parameters"] as JObject ?? [],
            };

            modules.Add(key, Evaluate(nestedTemplate, nestedInputs, ScopeOf(deployment, context), moduleFile));
        }

        return modules.ToImmutable();
    }

    private static JObject CollectOutputs(JObject template)
    {
        var result = new JObject();

        if (template[TestTargetType.OutputsPropertyName] is JObject declared)
        {
            foreach (var output in declared.Properties())
            {
                result[output.Name] = (output.Value as JObject)?["value"] ?? JValue.CreateNull();
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the instances out of one evaluated template. Loops have already been expanded by the
    /// evaluator, so the work here is deciding what counts as a deployed instance and attributing each
    /// one to the declaration it came from.
    /// </summary>
    private ImmutableArray<TestEvaluatedResource> Collect(EvaluatedDeployment deployment, string instancePrefix, bool recurse)
    {
        if (deployment.Template[TestTargetType.ResourcesPropertyName] is not JObject resources)
        {
            // A template that declares nothing emits an empty array, which carries no attribution problem.
            // Anything else non-symbolic cannot be mapped back to declarations.
            if (deployment.Template[TestTargetType.ResourcesPropertyName] is null or JArray { Count: 0 })
            {
                return [];
            }

            throw new InvalidOperationException("The evaluated template does not use symbolic resource names, so its instances cannot be attributed to declarations.");
        }

        var collected = ImmutableArray.CreateBuilder<TestEvaluatedResource>();

        foreach (var property in resources.Properties())
        {
            if (property.Value is not JObject resource)
            {
                continue;
            }

            var symbolicName = BaseSymbolicName(property.Name);

            // An existing reference reads state that something else owns; it is not deployed by this file.
            if (resource["existing"] is { Type: JTokenType.Boolean } existing && existing.Value<bool>())
            {
                continue;
            }

            if (resource["condition"] is { } condition)
            {
                if (condition.Type is not JTokenType.Boolean)
                {
                    // An unevaluatable condition is not a false one. Saying "not deployed" here would let a
                    // policy pass by describing a smaller deployment than the case actually produces.
                    throw new InvalidOperationException($"The condition on '{symbolicName}' could not be evaluated for this case.");
                }

                if (!condition.Value<bool>())
                {
                    continue;
                }
            }

            var type = resource[TestTargetType.TypePropertyName]?.Value<string>() ?? string.Empty;
            var instanceId = instancePrefix + property.Name;

            if (deployment.Modules.TryGetValue(property.Name, out var module))
            {
                if (recurse)
                {
                    collected.AddRange(Collect(module, $"{instanceId}/", recurse: true));
                }

                continue;
            }

            var (declaringFile, line) = ResolveDeclaration(deployment.File, symbolicName);

            collected.Add(new TestEvaluatedResource(
                resource[TestTargetType.NamePropertyName]?.Value<string>() ?? string.Empty,
                type,
                symbolicName,
                instanceId,
                declaringFile,
                line));
        }

        return collected.ToImmutable();
    }

    /// <summary>
    /// The deployment resources of an evaluated template, in declaration order. A false condition is
    /// skipped here as well: a module that is not deployed contributes neither instances nor outputs.
    /// </summary>
    private static IEnumerable<(string Key, JObject Deployment)> Deployments(JObject template)
    {
        if (template[TestTargetType.ResourcesPropertyName] is not JObject resources)
        {
            yield break;
        }

        foreach (var property in resources.Properties())
        {
            if (property.Value is not JObject resource ||
                !string.Equals(resource[TestTargetType.TypePropertyName]?.Value<string>(), DeploymentResourceType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (resource["condition"] is { Type: JTokenType.Boolean } condition && !condition.Value<bool>())
            {
                continue;
            }

            yield return (property.Name, resource);
        }
    }

    /// <summary>
    /// A module deployed to another scope is evaluated in that scope, so the case's own context is not a
    /// blanket override of where the module's resources actually go.
    /// </summary>
    private static TestDeploymentContext ScopeOf(JObject deployment, TestDeploymentContext context)
    {
        var scoped = context;

        if (deployment["subscriptionId"]?.Value<string>() is { } subscriptionId)
        {
            scoped = scoped with { SubscriptionId = subscriptionId };
        }

        if (deployment["resourceGroup"]?.Value<string>() is { } resourceGroup)
        {
            scoped = scoped with { ResourceGroup = resourceGroup };
        }

        // A module's own deployment name is the name its declaration computed, never the root's.
        scoped = scoped with { DeploymentName = deployment["name"]?.Value<string>() };

        return scoped;
    }

    private string? TryGetModuleFile(string file, string symbolicName)
        => sourceFacts.WithModules.Modules
            .FirstOrDefault(module => module.File == file && module.Name == symbolicName)
            ?.ResolvedFile is { Length: > 0 } resolved ? resolved : null;

    /// <summary>
    /// Maps an emitted resource back to the declaration that produced it. A child resource is emitted
    /// under its full path, so the innermost segment is what the declaring file named it.
    /// </summary>
    private (string File, int Line) ResolveDeclaration(string file, string symbolicName)
    {
        var candidates = sourceFacts.WithModules.Resources.Where(resource => resource.File == file).ToArray();
        var match = candidates.FirstOrDefault(resource => resource.Name == symbolicName)
            ?? candidates.FirstOrDefault(resource => resource.Name == LastSegment(symbolicName));

        return match is null ? (file, 0) : (match.File, match.Line);
    }

    private static string BaseSymbolicName(string key)
    {
        var index = key.IndexOf('[');

        return index < 0 ? key : key[..index];
    }

    private static string LastSegment(string symbolicName)
    {
        var index = symbolicName.LastIndexOf("::", StringComparison.Ordinal);

        return index < 0 ? symbolicName : symbolicName[(index + 2)..];
    }
}
