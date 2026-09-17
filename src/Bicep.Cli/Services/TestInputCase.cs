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
/// <param name="Name">The case name, unique within its file.</param>
/// <param name="Values">The evaluated value of each input the case assigns.</param>
public record TestInputCase(IOUri InputFile, string Name, ImmutableDictionary<string, JToken> Values)
{
    public string InputFileName
    {
        get
        {
            var segments = InputFile.PathSegments;

            return segments.Length > 0 ? segments[^1] : InputFile.Path;
        }
    }
}
