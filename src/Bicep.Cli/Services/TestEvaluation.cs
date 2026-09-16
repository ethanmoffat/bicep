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
}
