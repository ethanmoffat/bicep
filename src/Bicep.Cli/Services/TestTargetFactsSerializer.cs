// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TestFramework;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Renders collected target facts as the plain data an assertion expression sees. The property names
/// here are the same ones the compiler declares on the 'target' type, so what an author can write and
/// what the evaluator supplies cannot drift apart.
/// </summary>
public static class TestTargetFactsSerializer
{
    public static JObject Serialize(TestTargetFacts facts) => new()
    {
        [TestTargetType.ResourcesPropertyName] = SerializeResources(facts.Local),
        [TestTargetType.ModulesPropertyName] = SerializeModules(facts.Local),
        [TestTargetType.ImportsPropertyName] = SerializeImports(facts.Local),
        [TestTargetType.ParametersPropertyName] = SerializeParameters(facts.Local),
        [TestTargetType.OutputsPropertyName] = SerializeOutputs(facts.Local),
        [TestTargetType.TargetScopePropertyName] = facts.TargetScope,
        [TestTargetType.WithModulesPropertyName] = new JObject
        {
            [TestTargetType.ResourcesPropertyName] = SerializeResources(facts.WithModules),
            [TestTargetType.ModulesPropertyName] = SerializeModules(facts.WithModules),
            [TestTargetType.ImportsPropertyName] = SerializeImports(facts.WithModules),
            [TestTargetType.ParametersPropertyName] = SerializeParameters(facts.WithModules),
            [TestTargetType.OutputsPropertyName] = SerializeOutputs(facts.WithModules),
        },
    };

    private static JArray SerializeParameters(TestFactSet facts) => new(facts.Parameters.Select(parameter => new JObject
    {
        [TestTargetType.NamePropertyName] = parameter.Name,
        [TestTargetType.TypePropertyName] = parameter.Type,
        [TestTargetType.RequiredPropertyName] = parameter.Required,
        [TestTargetType.HasDefaultPropertyName] = parameter.HasDefault,
        [TestTargetType.FilePropertyName] = parameter.File,
        [TestTargetType.LinePropertyName] = parameter.Line,
    }).ToArray<object>());

    private static JArray SerializeOutputs(TestFactSet facts) => new(facts.Outputs.Select(output => new JObject
    {
        [TestTargetType.NamePropertyName] = output.Name,
        [TestTargetType.TypePropertyName] = output.Type,
        [TestTargetType.FilePropertyName] = output.File,
        [TestTargetType.LinePropertyName] = output.Line,
    }).ToArray<object>());

    private static JArray SerializeResources(TestFactSet facts) => new(facts.Resources.Select(resource => new JObject
    {
        [TestTargetType.SymbolicNamePropertyName] = resource.Name,
        [TestTargetType.TypePropertyName] = resource.Type,
        [TestTargetType.ExistingPropertyName] = resource.Existing,
        [TestTargetType.FilePropertyName] = resource.File,
        [TestTargetType.LinePropertyName] = resource.Line,
        [TestTargetType.WaitsForPropertyName] = new JArray(resource.WaitsFor.ToArray<object>()),
    }).ToArray<object>());

    private static JArray SerializeModules(TestFactSet facts) => new(facts.Modules.Select(module => new JObject
    {
        [TestTargetType.SymbolicNamePropertyName] = module.Name,
        [TestTargetType.PathPropertyName] = module.Path,
        [TestTargetType.ResolvedFilePropertyName] = module.ResolvedFile,
        [TestTargetType.FilePropertyName] = module.File,
        [TestTargetType.LinePropertyName] = module.Line,
        [TestTargetType.WaitsForPropertyName] = new JArray(module.WaitsFor.ToArray<object>()),
    }).ToArray<object>());

    private static JArray SerializeImports(TestFactSet facts) => new(facts.Imports.Select(import => new JObject
    {
        [TestTargetType.PathPropertyName] = import.Path,
        [TestTargetType.ResolvedFilePropertyName] = import.ResolvedFile,
        [TestTargetType.SymbolsPropertyName] = new JArray(import.Symbols.ToArray<object>()),
        [TestTargetType.WildcardPropertyName] = import.Wildcard,
        [TestTargetType.FilePropertyName] = import.File,
        [TestTargetType.LinePropertyName] = import.Line,
    }).ToArray<object>());

    /// <summary>
    /// Renders the evaluated branch. Outputs and the module-inclusive collection are only computed when
    /// the assertion asked for them, so a policy about deployed instances is never failed by an output it
    /// never read, and a policy about the selected file alone is never failed by a module it never
    /// mentioned.
    /// </summary>
    public static JObject SerializeEvaluated(TestEvaluatedFactsProvider evaluated, bool includeOutputs, bool includeWithModules, bool includeBodies)
    {
        var result = new JObject
        {
            [TestTargetType.ResourcesPropertyName] = SerializeEvaluatedResources(evaluated.Local, includeBodies),
        };

        if (includeOutputs)
        {
            result[TestTargetType.OutputsPropertyName] = evaluated.Outputs;
        }

        if (includeWithModules)
        {
            result[TestTargetType.WithModulesPropertyName] = new JObject
            {
                [TestTargetType.ResourcesPropertyName] = SerializeEvaluatedResources(evaluated.WithModules, includeBodies),
            };
        }

        return result;
    }

    /// <summary>
    /// Bodies are rendered only for an assertion that reads one. The facts reach ARM as a single value,
    /// so a body that could not be computed cannot be left for the assertion to trip over only if it
    /// happens to look: it fails the read here, naming the instance, rather than letting an assertion
    /// compare against a value no deployment would produce.
    /// </summary>
    private static JArray SerializeEvaluatedResources(IEnumerable<TestEvaluatedResource> resources, bool includeBodies) => new(resources.Select(resource =>
    {
        var fact = new JObject
        {
            [TestTargetType.NamePropertyName] = resource.Name,
            [TestTargetType.TypePropertyName] = resource.Type,
            [TestTargetType.SymbolicNamePropertyName] = resource.SymbolicName,
            [TestTargetType.InstanceIdPropertyName] = resource.InstanceId,
            [TestTargetType.FilePropertyName] = resource.File,
            [TestTargetType.LinePropertyName] = resource.Line,
            [TestTargetType.IdPropertyName] = resource.Id,
            [TestTargetType.SubscriptionIdPropertyName] = resource.SubscriptionId,
            [TestTargetType.ResourceGroupPropertyName] = resource.ResourceGroup,
        };

        if (includeBodies)
        {
            if (resource.UnresolvedReason is { } reason)
            {
                throw new InvalidOperationException($"The properties of '{resource.InstanceId}' ({resource.File}({resource.Line})) could not be evaluated for this case: {reason}");
            }

            foreach (var body in resource.Body.Properties())
            {
                fact[body.Name] = body.Value.DeepClone();
            }
        }

        return fact;
    }).ToArray<object>());
}
