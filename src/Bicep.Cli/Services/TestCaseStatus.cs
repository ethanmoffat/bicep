// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Cli.Services;

/// <summary>
/// The outcome of evaluating a single test case.
/// </summary>
public enum TestCaseStatus
{
    /// <summary>
    /// The target was evaluated and every assertion held.
    /// </summary>
    Passed,

    /// <summary>
    /// The target was evaluated and at least one assertion did not hold.
    /// </summary>
    Failed,

    /// <summary>
    /// The target could not be evaluated, so its assertions were never reached. This is a distinct
    /// category from <see cref="Failed"/> because the two call for different action - a failure says
    /// the policy was broken, an error says the policy never ran - but both are failures of the run.
    /// It is deliberately not called "skipped": nothing here declines to run a test, and a name that
    /// CI systems treat as benign would let a suite whose targets all failed to compile publish green.
    /// </summary>
    Errored,
}
