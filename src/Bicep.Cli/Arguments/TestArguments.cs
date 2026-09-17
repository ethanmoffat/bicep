// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Bicep.Cli.Arguments;

public record TestArguments(
    string? InputFile,
    string? FilePattern,
    bool NoRestore,
    bool List,
    ImmutableArray<string> Inputs,
    TestOutputFormat? OutputFormat,
    string? ResultsFile,
    DiagnosticsFormat? DiagnosticsFormat) : IFilePatternInputArguments;
