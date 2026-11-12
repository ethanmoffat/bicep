// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;
/// <summary>
/// One resource instance a target would deploy for a particular input case.
///
/// This is not an Azure inventory entry: it is what the compiler and the offline evaluator can work
/// out from the source, the case values and the deployment context, with nothing deployed or queried.
/// </summary>
/// <param name="InstanceId">
/// Distinguishes the instances one declaration produced. A loop contributes its index and a resource
/// reached through a module carries the module call it came through, so two calls to the same module
/// never collapse into one finding.
/// </param>
/// <param name="Body">
/// The top-level body keys the author wrote, computed for this case. Never serialized into a report: it
/// is derived from parameter values and mock responses.
/// </param>
/// <param name="UnresolvedReason">
/// Why this instance's properties could not be computed offline, or null if they were. The instance
/// itself still exists; only reading its body fails.
/// </param>
/// <param name="Id">
/// The resource ID Azure would address the instance by, built from the scope it deploys to: the case's
/// deployment context, a module's own scope, a declaration's own subscription or resource group, or
/// the resource an extension resource is scoped to.
/// </param>
/// <param name="SubscriptionId">The subscription the instance lives in, or empty at tenant or management group scope.</param>
/// <param name="ResourceGroup">The resource group the instance lives in, or empty if it is not deployed into one.</param>
/// <param name="Scope">
/// The ID of what the instance is deployed into, the prefix of <paramref name="Id"/>: the resource an
/// extension resource is scoped to, otherwise its resource group, subscription, management group or the
/// tenant (<c>/</c>).
/// </param>
public record TestEvaluatedResource(
    string Name,
    string Type,
    string SymbolicName,
    string InstanceId,
    string File,
    int Line,
    JObject Body,
    string? UnresolvedReason,
    string Id,
    string SubscriptionId,
    string ResourceGroup,
    string Scope);

/// <summary>
/// One module call a target would make for a particular input case: the deployment ARM would create for
/// it, with the name and scope the call computes. What the module itself deploys is reported as
/// <see cref="TestEvaluatedResource"/> instances.
/// </summary>
/// <param name="Name">The deployment name the call computes for this case.</param>
/// <param name="InstanceId">Distinguishes the calls one declaration produced, as for resource instances.</param>
/// <param name="Id">The resource ID of the deployment.</param>
/// <param name="SubscriptionId">The subscription the deployment runs in, or empty at tenant or management group scope.</param>
/// <param name="ResourceGroup">The resource group the deployment runs in, or empty if it does not run in one.</param>
/// <param name="Scope">The ID of the resource group, subscription or management group the deployment runs in, or <c>/</c> for the tenant.</param>
public record TestEvaluatedModule(
    string Name,
    string SymbolicName,
    string InstanceId,
    string File,
    int Line,
    string Id,
    string SubscriptionId,
    string ResourceGroup,
    string Scope);