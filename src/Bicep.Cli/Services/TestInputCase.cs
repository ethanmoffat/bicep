// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.IO.Abstraction;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// One named set of input values supplied to a test file by a test parameters file.
/// </summary>
/// <param name="InputFile">The file the case was declared in.</param>
/// <param name="Name">
/// The case name, unique within its file. Null when this stands for the whole input file, which could
/// supply no cases at all: its failure is still reported against the input file it belongs to.
/// </param>
/// <param name="Values">The evaluated value of each input the case assigns.</param>
/// <param name="Context">The ambient deployment context this case is evaluated with.</param>
public record TestInputCase(IOUri InputFile, string? Name, ImmutableDictionary<string, JToken> Values, TestDeploymentContext Context)
{
    /// <summary>
    /// Why this case cannot supply values, or null when it can. A case that cannot is still a case: it
    /// is reported under its own name for every evaluation it would have run, so one broken case never
    /// hides its siblings and is never silently dropped from the results.
    /// </summary>
    public string? Error { get; init; }

    public static TestInputCase Failed(IOUri inputFile, string? name, string error)
        => new(inputFile, name, [], TestDeploymentContext.Empty) { Error = error };

    public string InputFileName
    {
        get
        {
            var segments = InputFile.PathSegments;

            return segments.Length > 0 ? segments[^1] : InputFile.Path;
        }
    }

    /// <summary>
    /// What a case adds to the identity of an evaluation: the input file, and the case when there is
    /// one. An input file that could supply no cases is named on its own.
    /// </summary>
    public string CaseIdSuffix => Name is { } name ? $"{InputFileName}#{name}" : InputFileName;

    /// <summary>
    /// How the console names the case: its file, and its name when it has one.
    /// </summary>
    public string DisplayName => Name is { } name ? $"{InputFileName}: {name}" : InputFileName;
}
