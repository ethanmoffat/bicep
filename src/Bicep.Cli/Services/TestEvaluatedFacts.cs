// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
public record TestEvaluatedResource(
    string Name,
    string Type,
    string SymbolicName,
    string InstanceId,
    string File,
    int Line);
