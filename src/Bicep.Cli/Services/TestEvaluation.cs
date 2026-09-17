// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Azure.Deployments.Core.Definitions.Schema;


namespace Bicep.Cli.Services;

public record TestEvaluation(
    Template? Template,
    String? Error,
    ImmutableArray<AssertionResult> AllAssertions,
    ImmutableArray<AssertionResult> FailedAssertions)
{

    public bool Success => Error == null && (FailedAssertions.Length == 0);

    public bool Skip => Error != null;

    /// <summary>
    /// The outcome of this evaluation. An evaluation that could not run at all is skipped rather
    /// than failed, because its assertions were never reached.
    /// </summary>
    public TestCaseStatus Status => Skip
        ? TestCaseStatus.Skipped
        : FailedAssertions.Length > 0
            ? TestCaseStatus.Failed
            : TestCaseStatus.Passed;
}

public record AssertionResult(string Source, bool Result)
{
    /// <summary>
    /// The author-supplied explanation of what to do when this assertion fails.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The source locations of the facts that violated the assertion, so a failure identifies the
    /// offending declarations rather than only counting them.
    /// </summary>
    public ImmutableArray<string> Violations { get; init; } = [];

    /// <summary>
    /// Why the assertion could not be evaluated. An assertion that could not run fails; it never passes
    /// by default.
    /// </summary>
    public string? Error { get; init; }
}
