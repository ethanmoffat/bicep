// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Azure.Deployments.Core;
using Azure.Deployments.Core.Components;
using Azure.Deployments.Core.Configuration;
using Azure.Deployments.Core.Definitions;
using Azure.Deployments.Core.Definitions.Extensibility;
using Azure.Deployments.Core.Definitions.Schema;
using Azure.Deployments.Core.Diagnostics;
using Azure.Deployments.Core.ErrorResponses;
using Azure.Deployments.Expression.Engines;
using Azure.Deployments.Expression.Expressions;
using Azure.Deployments.Expression.Intermediate;
using Azure.Deployments.Expression.Intermediate.Extensions;
using Azure.Deployments.Templates.Engines;
using Azure.Deployments.Templates.Expressions;
using Azure.Deployments.Templates.Expressions.PartialEvaluation;
using Azure.Deployments.Templates.Interfaces;
using Bicep.Core.Emit;
using Bicep.Core.Features;
using Microsoft.WindowsAzure.ResourceStack.Common.Collections;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Newtonsoft.Json.Linq;
using FunctionExpression = Azure.Deployments.Expression.Expressions.FunctionExpression;
using IntermediateEvaluationContext = Azure.Deployments.Expression.Intermediate.ExpressionEvaluationContext;

namespace Bicep.Core.Utils
{
    public partial class TemplateEvaluator
    {
        private class NoOpTemplateMetricRecorder : ITemplateMetricsRecorder
        {
            public static readonly NoOpTemplateMetricRecorder Instance = new();

            public void Record(MetricDatum metricDatum)
            {
            }
        }

        /// <summary>
        /// Which resources' properties are still unknown while resources are evaluated one at a time, and
        /// why the last attempt at each failed. A resource that is not here has been computed.
        /// </summary>
        private sealed class PendingResourceBodies
        {
            public Dictionary<TemplateResource, Exception?> Pending { get; } = new(ReferenceEqualityComparer.Instance);
        }

        private class TemplateEvaluationContext : IEvaluationContext
        {
            private readonly IEvaluationContext context;
            private readonly OrdinalInsensitiveDictionary<TemplateResource> resourceLookup;
            private readonly OrdinalInsensitiveDictionary<string> symbolicResourceIds;
            private readonly EvaluationConfiguration config;
            private readonly PendingResourceBodies? bodies;

            private TemplateEvaluationContext(IEvaluationContext context, ExpressionScope scope, OrdinalInsensitiveDictionary<TemplateResource> resourceLookup, OrdinalInsensitiveDictionary<string> symbolicResourceIds, EvaluationConfiguration config, PendingResourceBodies? bodies)
            {
                this.context = context;
                this.Scope = scope;
                this.resourceLookup = resourceLookup;
                this.symbolicResourceIds = symbolicResourceIds;
                this.config = config;
                this.bodies = bodies;
            }

            /// <summary>
            /// <paramref name="copyContext"/> is the loop position of the resource whose expressions are
            /// about to be evaluated. It is what copyIndex() reads, so an expression belonging to a
            /// copy-expanded resource must be evaluated in a context built for that resource.
            /// </summary>
            public static TemplateEvaluationContext Create(Template template, OrdinalInsensitiveDictionary<TemplateResource> resourceLookup, OrdinalInsensitiveDictionary<string> symbolicResourceIds, EvaluationConfiguration config, TemplateCopyContext? copyContext = null, PendingResourceBodies? bodies = null)
            {
                var context = TemplateEngine.GetExpressionEvaluationContext(
                    config.ManagementGroup,
                    config.SubscriptionId,
                    config.ResourceGroup,
                    template,
                    NoOpTemplateMetricRecorder.Instance,
                    copyContext: copyContext,
                    onGetExtension: static (_, _) => null);

                return new TemplateEvaluationContext(context, context.Scope, resourceLookup, symbolicResourceIds, config, bodies);
            }

            public bool IsShortCircuitAllowed => this.context.IsShortCircuitAllowed;

            public ExpressionScope Scope { get; }

            public bool AllowInvalidProperty(Exception exception, FunctionExpression functionExpression, FunctionArgument[] functionParametersValues, JToken[] selectedProperties) =>
                this.context.AllowInvalidProperty(exception, functionExpression, functionParametersValues, selectedProperties);

            public JToken EvaluateFunction(FunctionExpression functionExpression, FunctionArgument[] parameters, IEvaluationContext context, TemplateErrorAdditionalInfo? additionalnfo)
            {
                if (functionExpression.Function.StartsWithOrdinalInsensitively(LanguageConstants.ListFunctionPrefix) &&
                    (this.config.OnListFunc is not null || this.config.OnMockedRequestFunc is not null))
                {
                    var resourceId = parameters[0].TryGetToken()?.Value<string>() ?? throw new UnreachableException();
                    var apiVersion = parameters[1].TryGetToken()?.Value<string>() ?? throw new UnreachableException();
                    var body = parameters.Length > 2 ? parameters[2].TryGetToken() : null;
                    var target = ResolveRequestTarget(resourceId);

                    if (this.config.OnMockedRequestFunc?.Invoke(functionExpression.Function, target.ResourceId, apiVersion, body, fullBody: false) is { } mocked)
                    {
                        return mocked;
                    }

                    if (this.config.OnListFunc is not null)
                    {
                        return this.config.OnListFunc(functionExpression.Function, resourceId, apiVersion, body);
                    }

                    // A list operation is answered by the resource provider, never by the template, so an
                    // unanswered one has no offline result at all.
                    this.config.OnUnansweredRequestFunc?.Invoke(functionExpression.Function, target.ResourceId, apiVersion);
                }

                if (functionExpression.Function.EqualsOrdinalInsensitively("reference"))
                {
                    var resourceId = parameters[0].TryGetToken()?.Value<string>() ?? throw new UnreachableException();
                    var apiVersion = parameters.Length > 1 ? (parameters[1].TryGetToken()?.Value<string>() ?? throw new UnreachableException()) : null;
                    var fullBody = parameters.Length > 2 && parameters[2].TryGetToken()?.Value<string>() is { } fullBodyParam && StringComparer.OrdinalIgnoreCase.Equals(fullBodyParam, "Full");

                    if (this.config.OnMockedRequestFunc is not null || this.config.OnUnansweredRequestFunc is not null)
                    {
                        // A reference issued without an api version still has one; it is just spelled in the
                        // declaration rather than at the call site. Resolving it from the template, and then
                        // from the configured answers, keeps matching exact instead of version-agnostic.
                        var target = ResolveRequestTarget(resourceId);
                        var resolved = apiVersion
                            ?? target.ApiVersion
                            ?? this.config.ResolveMockedApiVersionFunc?.Invoke("reference", target.ResourceId);

                        if (resolved is not null &&
                            this.config.OnMockedRequestFunc?.Invoke("reference", target.ResourceId, resolved, requestBody: null, fullBody) is { } mocked)
                        {
                            return mocked;
                        }

                        // An existing resource is read, not deployed, so this template never computes its
                        // runtime state. Without an answer there is nothing to report but the declaration.
                        if (target.Resource?.Existing?.Value == true)
                        {
                            this.config.OnUnansweredRequestFunc?.Invoke("reference", target.ResourceId, resolved ?? target.ApiVersion);
                        }
                    }

                    if (apiVersion is not null && this.config.OnReferenceFunc is not null)
                    {
                        return this.config.OnReferenceFunc(resourceId, apiVersion, fullBody);
                    }

                    // Evaluating resources one at a time, a resource this template deploys answers with
                    // the properties it declares, once they are computed. One that is not computed cannot
                    // answer, and saying so fails only the read rather than handing over a placeholder.
                    // Existing resources and module deployments answer as they always have.
                    if (this.bodies is not null &&
                        ResolveRequestTarget(resourceId).Resource is { } deployed &&
                        deployed.Existing?.Value != true &&
                        !deployed.Type.Value.EqualsOrdinalInsensitively(DeploymentResourceType))
                    {
                        if (this.bodies.Pending.TryGetValue(deployed, out var failure))
                        {
                            throw new Azure.Deployments.Core.Exceptions.ExpressionException(failure is null
                                ? $"The properties of '{deployed.SymbolicName}' have not been computed yet."
                                : $"The properties of '{deployed.SymbolicName}' could not be computed offline: {failure.Message}");
                        }

                        return fullBody ? deployed.ToJToken() : deployed.Properties?.Value ?? new JObject();
                    }

                    if (this.resourceLookup.TryGetValue(resourceId, out var foundResource) &&
                        (apiVersion is null || StringComparer.OrdinalIgnoreCase.Equals(apiVersion, foundResource.ApiVersion.Value)))
                    {
                        return fullBody ? foundResource.ToJToken() : foundResource.Properties.ToJToken();
                    }

                    if (this.config.OnUnresolvedReferenceFunc?.Invoke(resourceId, apiVersion, fullBody) is { } supplied)
                    {
                        return supplied;
                    }
                }

                return this.context.EvaluateFunction(functionExpression, parameters, context, additionalnfo);
            }

            public bool ShouldIgnoreExceptionDuringEvaluation(Exception exception) =>
                this.context.ShouldIgnoreExceptionDuringEvaluation(exception);

            /// <summary>
            /// States a runtime read in terms of what it addresses rather than how it was written. Symbolic
            /// name codegen spells a read as the declaration that produced it, so translating back to the
            /// resource ID keeps a configured answer independent of which codegen the target compiled with.
            /// </summary>
            private (string ResourceId, string? ApiVersion, TemplateResource? Resource) ResolveRequestTarget(string reference)
            {
                if (this.symbolicResourceIds.TryGetValue(reference, out var symbolicId) &&
                    this.resourceLookup.TryGetValue(symbolicId, out var symbolic))
                {
                    return (symbolicId, symbolic.ApiVersion.Value, symbolic);
                }

                return this.resourceLookup.TryGetValue(reference, out var declared)
                    ? (reference, declared.ApiVersion.Value, declared)
                    : (reference, null, null);
            }

            public IEvaluationContext WithNewScope(ExpressionScope scope) => new TemplateEvaluationContext(this.context, scope, this.resourceLookup, this.symbolicResourceIds, this.config, this.bodies);
        }

        private static readonly string DummyTenantId = Guid.Empty.ToString();
        private static readonly string DummyManagementGroupName = Guid.Empty.ToString();
        private static readonly string DummySubscriptionId = Guid.Empty.ToString();
        private const string DummyResourceGroupName = "DummyResourceGroup";
        private const string DummyLocation = "Dummy Location";

        [GeneratedRegex(@"https?://schema\.management\.azure\.com/schemas/[0-9a-zA-Z-]+/(?<templateType>[a-zA-Z]+)Template\.json#?", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex templateSchemaPattern();
        public delegate JToken OnListDelegate(string functionName, string resourceId, string apiVersion, JToken? body);

        public delegate JToken OnReferenceDelegate(string resourceId, string apiVersion, bool fullBody);

        /// <summary>
        /// Supplies a configured answer for a runtime read, or returns null to leave the evaluator's own
        /// resolution in place. The api version of a reference issued without one is resolved before the
        /// call rather than guessed here.
        /// </summary>
        public delegate JToken? OnMockedRequestDelegate(string operation, string resourceId, string apiVersion, JToken? requestBody, bool fullBody);

        /// <summary>
        /// Reports the api version a configured answer would use for a resource, when the caller did not
        /// state one. Returning null means the request cannot be attributed to a single configured answer.
        /// </summary>
        public delegate string? ResolveMockedApiVersionDelegate(string operation, string resourceId);

        /// <summary>
        /// Reports a runtime read that nothing answered. Implementations raise; returning simply leaves the
        /// evaluator's own behavior in place.
        /// </summary>
        public delegate void OnUnansweredRequestDelegate(string operation, string resourceId, string? apiVersion);

        /// <summary>
        /// Supplies a reference the template itself cannot resolve, such as a symbolic reference to a
        /// nested deployment whose outputs were computed separately. Returning null leaves the
        /// reference unresolved so the engine's own behavior is preserved.
        /// </summary>
        public delegate JToken? OnUnresolvedReferenceDelegate(string reference, string? apiVersion, bool fullBody);

        /// <summary>
        /// Reports that the properties of one resource could not be computed. <paramref name="symbolicName"/>
        /// is the key the resource is emitted under.
        /// </summary>
        public delegate void OnUnresolvedResourcePropertiesDelegate(string symbolicName, Exception exception);

        public record EvaluationConfiguration(
            string TenantId,
            string ManagementGroup,
            string SubscriptionId,
            string ResourceGroup,
            string RgLocation,
            Dictionary<string, JToken> Metadata,
            OnListDelegate? OnListFunc,
            OnReferenceDelegate? OnReferenceFunc)
        {
            public OnUnresolvedReferenceDelegate? OnUnresolvedReferenceFunc { get; init; }

            public OnMockedRequestDelegate? OnMockedRequestFunc { get; init; }

            public ResolveMockedApiVersionDelegate? ResolveMockedApiVersionFunc { get; init; }

            public OnUnansweredRequestDelegate? OnUnansweredRequestFunc { get; init; }

            /// <summary>
            /// Evaluates outputs strictly, so an expression that cannot be computed is reported rather
            /// than left in place as its own unevaluated text.
            /// </summary>
            public bool StrictOutputs { get; init; }

            /// <summary>
            /// Leaves a resource value that cannot yet be computed in place instead of failing, so a
            /// caller resolving values across nested deployments can evaluate again with more known.
            /// </summary>
            public bool TolerateUnresolvedValues { get; init; }

            /// <summary>
            /// Reports a resource whose properties cannot be computed, instead of failing the whole
            /// template. The resource keeps whatever could be computed; the caller decides whether that
            /// resource's values matter. Ignored when <see cref="TolerateUnresolvedValues"/> is set.
            /// </summary>
            public OnUnresolvedResourcePropertiesDelegate? OnUnresolvedResourcePropertiesFunc { get; init; }

            public static EvaluationConfiguration Default = new(
                DummyTenantId,
                DummyManagementGroupName,
                DummySubscriptionId,
                DummyResourceGroupName,
                DummyLocation,
                new(),
                null,
                null
            );
        }

        /// <summary>
        /// The resource ID a resource is addressed by. A declaration may state a subscription or
        /// resource group of its own, which is where the resource actually lives; using the deployment's
        /// scope for it would address something else entirely.
        /// </summary>
        private static string GetResourceId(string scopeString, string subscriptionId, TemplateResource resource)
        {
            var typeSegments = resource.Type.Value.Split('/');
            var nameSegments = resource.Name.Value.Split('/');

            var types = new[] { typeSegments.First() }
                .Concat(typeSegments.Skip(1).Zip(nameSegments, (type, name) => $"{type}/{name}"));

            return $"{GetScopeString(scopeString, subscriptionId, resource)}providers/{string.Join('/', types)}";
        }

        private static string GetScopeString(string deploymentScopeString, string subscriptionId, TemplateResource resource)
        {
            var declaredSubscriptionId = resource.SubscriptionId?.Value;
            var declaredResourceGroup = resource.ResourceGroup?.Value;

            if (declaredResourceGroup is { Length: > 0 })
            {
                return $"/subscriptions/{(declaredSubscriptionId is { Length: > 0 } ? declaredSubscriptionId : subscriptionId)}/resourceGroups/{declaredResourceGroup}/";
            }

            return declaredSubscriptionId is { Length: > 0 }
                ? $"/subscriptions/{declaredSubscriptionId}/"
                : deploymentScopeString;
        }

        private static string GetDeploymentScopeString(TemplateDeploymentScope deploymentScope, EvaluationConfiguration config) => deploymentScope switch
        {
            TemplateDeploymentScope.Tenant => "/",
            TemplateDeploymentScope.ManagementGroup => $"/providers/Microsoft.Management/managementGroups/{config.ManagementGroup}/",
            TemplateDeploymentScope.Subscription => $"/subscriptions/{config.SubscriptionId}/",
            TemplateDeploymentScope.ResourceGroup => $"/subscriptions/{config.SubscriptionId}/resourceGroups/{config.ResourceGroup}/",
            _ => throw new InvalidOperationException(),
        };

        /// <summary>
        /// The ARM resource ID of one resource from an evaluated template, as Azure would address it once
        /// deployed. Unlike the lookup used to answer <c>reference()</c>, this honours an extension
        /// resource's <c>scope</c>, so a role assignment is addressed beneath what it is assigned on, and
        /// addresses a resource group the way Azure does rather than as a provider resource.
        /// </summary>
        public static string GetEvaluatedResourceId(JToken template, JObject resource, EvaluationConfiguration config)
        {
            var deploymentScope = GetDeploymentScope(template["$schema"]?.Value<string>() ?? string.Empty);
            var deploymentScopeString = GetDeploymentScopeString(deploymentScope, config);
            var declaredSubscriptionId = resource["subscriptionId"]?.Value<string>();
            var declaredResourceGroup = resource["resourceGroup"]?.Value<string>();
            var type = resource["type"]?.Value<string>() ?? string.Empty;
            var name = resource["name"]?.Value<string>() ?? string.Empty;

            string scopeString;

            if (resource["scope"] is { Type: JTokenType.String } scopeToken && scopeToken.Value<string>() is { Length: > 0 } scope)
            {
                // A relative scope names a resource in the deployment's own scope.
                scopeString = scope.StartsWith('/') ? scope : $"{deploymentScopeString}providers/{scope}";
                scopeString = scopeString.EndsWith('/') ? scopeString : scopeString + "/";
            }
            else if (declaredResourceGroup is { Length: > 0 })
            {
                scopeString = $"/subscriptions/{(declaredSubscriptionId is { Length: > 0 } ? declaredSubscriptionId : config.SubscriptionId)}/resourceGroups/{declaredResourceGroup}/";
            }
            else
            {
                scopeString = declaredSubscriptionId is { Length: > 0 } ? $"/subscriptions/{declaredSubscriptionId}/" : deploymentScopeString;
            }

            if (string.Equals(type, "Microsoft.Resources/resourceGroups", StringComparison.OrdinalIgnoreCase) &&
                scopeString.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase) &&
                scopeString.Count(c => c == '/') == 3)
            {
                return $"{scopeString}resourceGroups/{name}";
            }

            var typeSegments = type.Split('/');
            var nameSegments = name.Split('/');
            var types = new[] { typeSegments.First() }
                .Concat(typeSegments.Skip(1).Zip(nameSegments, (typeSegment, nameSegment) => $"{typeSegment}/{nameSegment}"));

            return $"{scopeString}providers/{string.Join('/', types)}";
        }

        private static void ProcessTemplateLanguageExpressions(Template template, EvaluationConfiguration config, TemplateDeploymentScope deploymentScope)
        {
            var scopeString = GetDeploymentScopeString(deploymentScope, config);

            var resourceLookup = template.Resources.ToOrdinalInsensitiveDictionary(x => GetResourceId(scopeString, config.SubscriptionId, x));
            var symbolicResourceIds = new OrdinalInsensitiveDictionary<string>();

            foreach (var resource in template.Resources)
            {
                if (resource.SymbolicName is { } symbolicName)
                {
                    symbolicResourceIds[symbolicName] = GetResourceId(scopeString, config.SubscriptionId, resource);
                }
            }

            // Resources evaluated one at a time track which are still unknown, so that reading one of those
            // fails the reader instead of handing it a placeholder.
            var bodies = config.OnUnresolvedResourcePropertiesFunc is not null && !config.TolerateUnresolvedValues ? new PendingResourceBodies() : null;
            var evaluationContext = TemplateEvaluationContext.Create(template, resourceLookup, symbolicResourceIds, config, bodies: bodies);

            // The copy has already been expanded into one resource per iteration, but the expressions
            // inside each one still say copyIndex(). Only a context built for this resource knows which
            // iteration it is, so a looped resource is evaluated in its own.
            IEvaluationContext ContextFor(TemplateResource resource) => resource.CopyContext is null
                ? evaluationContext
                : TemplateEvaluationContext.Create(template, resourceLookup, symbolicResourceIds, config, resource.CopyContext, bodies);

            if (bodies is not null && config.OnUnresolvedResourcePropertiesFunc is { } onUnresolved)
            {
                EvaluateResourcesIndividually(template, bodies, ContextFor, onUnresolved);
            }
            else
            {
                foreach (var resource in template.Resources)
                {
                    if (resource.Properties is null)
                    {
                        continue;
                    }

                    // A value a resource needs may come from a deployment this pass has not evaluated yet.
                    // A caller resolving that chain asks for tolerance and repeats; a caller that expects
                    // every value to be available gets the failure.
                    resource.Properties.Value = config.TolerateUnresolvedValues
                        ? ExpressionsEngine.EvaluateLanguageExpressionsOptimistically(
                            root: resource.Properties.Value,
                            evaluationContext: ContextFor(resource),
                            skipEvaluationPaths: SkipEvaluationPaths(resource))
                        : ExpressionsEngine.EvaluateLanguageExpressionsRecursive(
                            root: resource.Properties.Value,
                            evaluationContext: ContextFor(resource),
                            skipEvaluationPaths: SkipEvaluationPaths(resource));
                }
            }

            if (template.Outputs is not null && template.Outputs.Count > 0)
            {
                foreach (var outputKey in template.Outputs.Keys.ToList())
                {
                    // Optimistic evaluation leaves an expression it cannot compute as raw text, which reads
                    // as a successfully produced value. A caller that needs an output to mean what it says
                    // asks for strict evaluation and gets the failure instead.
                    template.Outputs[outputKey].Value.Value = config.StrictOutputs
                        ? ExpressionsEngine.EvaluateLanguageExpressionsRecursive(
                            root: template.Outputs[outputKey].Value.Value,
                            evaluationContext: evaluationContext)
                        : ExpressionsEngine.EvaluateLanguageExpressionsOptimistically(
                            root: template.Outputs[outputKey].Value.Value,
                            evaluationContext: evaluationContext);
                }
            }
        }

        private const string DeploymentResourceType = "Microsoft.Resources/deployments";

        /// <summary>
        /// A module's nested template is evaluated as its own deployment, not as part of the caller.
        /// </summary>
        private static InsensitiveHashSet SkipEvaluationPaths(TemplateResource resource)
        {
            var skipEvaluationPaths = new InsensitiveHashSet();

            if (resource.Type.Value.EqualsOrdinalInsensitively(DeploymentResourceType))
            {
                skipEvaluationPaths.Add("template");
            }

            return skipEvaluationPaths;
        }

        /// <summary>
        /// Evaluates each resource's properties on their own, so that one value only Azure knows fails
        /// that resource rather than the template. A resource may read another declared after it, so
        /// resources are retried from their original declarations until a round resolves nothing more.
        /// Whatever is left is reported and keeps its unevaluated declaration, which nothing can read.
        /// </summary>
        private static void EvaluateResourcesIndividually(
            Template template,
            PendingResourceBodies bodies,
            Func<TemplateResource, IEvaluationContext> contextFor,
            OnUnresolvedResourcePropertiesDelegate onUnresolved)
        {
            var declared = new Dictionary<TemplateResource, JToken>(ReferenceEqualityComparer.Instance);

            foreach (var resource in template.Resources)
            {
                if (resource.Properties is not null)
                {
                    declared[resource] = resource.Properties.Value.DeepClone();
                    bodies.Pending[resource] = null;
                }
            }

            for (var round = 0; bodies.Pending.Count > 0; round++)
            {
                var resolvedAny = false;

                foreach (var resource in template.Resources)
                {
                    if (!bodies.Pending.ContainsKey(resource))
                    {
                        continue;
                    }

                    try
                    {
                        resource.Properties.Value = ExpressionsEngine.EvaluateLanguageExpressionsRecursive(
                            root: declared[resource].DeepClone(),
                            evaluationContext: contextFor(resource),
                            skipEvaluationPaths: SkipEvaluationPaths(resource));

                        bodies.Pending.Remove(resource);
                        resolvedAny = true;
                    }
                    catch (Exception exception)
                    {
                        bodies.Pending[resource] = exception;
                    }
                }

                // The first round can fail a resource only because what it reads comes later, so the
                // second always runs: it either resolves it or records the reason that is real.
                if (!resolvedAny && round > 0)
                {
                    break;
                }
            }

            foreach (var (resource, failure) in bodies.Pending)
            {
                var exception = failure ?? throw new UnreachableException();

                if (resource.SymbolicName is not { } symbolicName)
                {
                    throw exception;
                }

                onUnresolved(symbolicName, exception);
                resource.Properties.Value = declared[resource];
            }
        }

        public static Template Evaluate(JToken? templateJtoken, JToken? parametersJToken = null, Func<EvaluationConfiguration, EvaluationConfiguration>? configBuilder = null, IFeatureProvider? features = null)
        {
            var configuration = EvaluationConfiguration.Default;

            if (configBuilder is not null)
            {
                configuration = configBuilder(configuration);
            }

            return EvaluateTemplate(templateJtoken, parametersJToken, configuration, features);
        }

        private static Template EvaluateTemplate(JToken? templateJtoken, JToken? parametersJToken, EvaluationConfiguration config, IFeatureProvider? features)
        {
            templateJtoken = templateJtoken ?? throw new ArgumentNullException(nameof(templateJtoken));

            var deploymentScope = GetDeploymentScope(templateJtoken["$schema"]!.ToString());

            var metadata = new InsensitiveDictionary<JToken>(config.Metadata);
            if (deploymentScope == TemplateDeploymentScope.Subscription || deploymentScope == TemplateDeploymentScope.ResourceGroup)
            {
                metadata["subscription"] = new JObject
                {
                    ["id"] = $"/subscriptions/{config.SubscriptionId}",
                    ["subscriptionId"] = config.SubscriptionId,
                    ["tenantId"] = config.TenantId,
                };
            }
            if (deploymentScope == TemplateDeploymentScope.ResourceGroup)
            {
                metadata["resourceGroup"] = new JObject
                {
                    ["id"] = $"/subscriptions/{config.SubscriptionId}/resourceGroups/{config.ResourceGroup}",
                    ["name"] = config.ResourceGroup,
                    ["location"] = config.RgLocation,
                };
            }
            ;
            if (deploymentScope == TemplateDeploymentScope.ManagementGroup)
            {
                metadata["managementGroup"] = new JObject
                {
                    ["id"] = $"/providers/Microsoft.Management/managementGroups/{config.ManagementGroup}",
                    ["name"] = config.ManagementGroup,
                    ["type"] = "Microsoft.Management/managementGroups",
                };
            }
            ;
            // tenant() function is available at all scopes
            metadata["tenant"] = new JObject
            {
                ["tenantId"] = config.TenantId,
            };

            try
            {
                var template = TemplateEngine.ParseTemplate(templateJtoken.ToString());
                var parameters = ConvertParameters(parametersJToken);
                var extensionConfigs = ConvertExtensionConfigs(parametersJToken);

                var expectedApiVersion = EmitConstants.NestedDeploymentResourceApiVersion;

                TemplateEngine.ValidateTemplate(template, expectedApiVersion, deploymentScope);

                TemplateEngine.ProcessTemplateLanguageExpressions(
                    managementGroupName: config.ManagementGroup,
                    subscriptionId: config.SubscriptionId,
                    resourceGroupName: config.ResourceGroup,
                    template: template,
                    apiVersion: expectedApiVersion,
                    inputParameters: new(parameters),
                    metadata: metadata,
                    templateExtResolver: new TemplateExtensionPreprocessingResolver(
                        template,
                        extensionConfigs,
                        new DeploymentParametersDefinition()),
                    metricsRecorder: new TemplateMetricsRecorder());

                ProcessTemplateLanguageExpressions(template, config, deploymentScope);

                TemplateEngine.ValidateProcessedTemplate(template, expectedApiVersion, deploymentScope);

                return template;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Evaluating template failed: {exception.Message}." +
                    $"\nTemplate file: {templateJtoken}" +
                    (parametersJToken is null ? "" : $"\nParameters file: {parametersJToken}"),
                    exception);
            }
        }

        private static ImmutableDictionary<string, JToken> ConvertParameters(JToken? parametersJToken)
        {
            if (parametersJToken is null)
            {
                return [];
            }

            var parametersObject = parametersJToken["parameters"] as JObject;

            var externalInputsObject = parametersJToken["externalInputs"] as JObject;
            var externalInputs = externalInputsObject?.Properties().ToImmutableDictionary(
                x => x.Name,
                x => new DeploymentExternalInput { Value = x.Value["value"] }) ?? [];

            IntermediateEvaluationContext context = new(
                [
                    ExpressionBuiltInFunctions.Functions,
                    new ParametersScope(externalInputs)
                ],
                new TemplateMetricsRecorder());

            return parametersObject!.Properties().ToImmutableDictionary(x => x.Name, x =>
            {
                if (x.Value["expression"] is { } expression)
                {
                    return ToJTokenExpressionSerializer.Serialize(context.EvaluateExpression(ExpressionParser.ParseLanguageExpression(expression)));
                }

                return x.Value["value"]!;
            });
        }

        private static OrdinalDictionary<OrdinalDictionary<DeploymentExtensionConfigItem>> ConvertExtensionConfigs(JToken? parametersJToken) =>
            parametersJToken?["extensionConfigs"] is JObject extensionConfigs
                ? extensionConfigs.FromDeploymentsJToken<OrdinalDictionary<OrdinalDictionary<DeploymentExtensionConfigItem>>>()
                : [];

        private static TemplateDeploymentScope GetDeploymentScope(string templateSchema)
        {
            var templateSchemaMatch = templateSchemaPattern().Match(templateSchema);
            var templateType = templateSchemaMatch.Groups["templateType"].Value.ToLowerInvariant();

            return templateType switch
            {
                "deployment" => TemplateDeploymentScope.ResourceGroup,
                "subscriptiondeployment" => TemplateDeploymentScope.Subscription,
                "managementgroupdeployment" => TemplateDeploymentScope.ManagementGroup,
                "tenantdeployment" => TemplateDeploymentScope.Tenant,
                _ => throw new InvalidOperationException($"Unrecognized schema: {templateSchema}"),
            };
        }
    }
}
