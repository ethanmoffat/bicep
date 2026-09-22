// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Cli.Arguments;

/// <summary>
/// How much of a run's result the test command writes to the console. This controls the human log
/// only: the machine-readable document always carries every case, because a host that receives a
/// filtered document cannot tell a case that passed from one that was never reported.
/// </summary>
public enum TestOutputDetail
{
    /// <summary>
    /// The counts line only. Enough to know whether the run passed and how much it did.
    /// </summary>
    Summary,

    /// <summary>
    /// The counts line, plus every case that failed or could not be evaluated. The default: a policy
    /// run over several hundred targets is read to find out what is wrong with it, and a thousand
    /// lines of confirmation that nothing is wrong buries the few lines that say something is.
    /// </summary>
    Failures,

    /// <summary>
    /// The counts line and every case, whatever its outcome.
    /// </summary>
    All,
}
