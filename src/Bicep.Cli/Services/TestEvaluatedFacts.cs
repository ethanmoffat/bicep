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
public record TestEvaluatedResource(
    string Name,
    string Type,
    string SymbolicName,
    string InstanceId,
    string File,
    int Line,
    JObject Body,
    string? UnresolvedReason);
