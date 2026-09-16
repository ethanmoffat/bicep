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
    /// The target could not be evaluated, so its assertions were never reached.
    /// </summary>
    Skipped,
}
