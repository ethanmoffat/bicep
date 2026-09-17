// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Cli.Arguments;

public record TestArguments(
    string? InputFile,
    string? FilePattern,
    bool NoRestore,
    bool List,
    DiagnosticsFormat? DiagnosticsFormat) : IFilePatternInputArguments;
