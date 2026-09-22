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
    TestOutputDetail? OutputDetail,
    string? ResultsFile,
    DiagnosticsFormat? DiagnosticsFormat) : IFilePatternInputArguments
{
    /// <summary>
    /// Decides whether what the caller gave names one file or matches many.
    ///
    /// The two are one argument because they answer one question - which tests to run - and a caller
    /// who has to know in advance which of two spellings their path needs gets an unhelpful
    /// file-not-found when they guess wrong. Glob metacharacters are not legal in a Bicep file name,
    /// so their presence is unambiguous.
    /// </summary>
    public static bool IsPattern(string input) => input.Contains('*') || input.Contains('?');
}
