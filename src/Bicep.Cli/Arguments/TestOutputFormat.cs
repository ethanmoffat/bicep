// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Cli.Arguments;

/// <summary>
/// How the test command reports its own inventory and results. Diagnostics formatting is a
/// separate concern, controlled by --diagnostics-format.
/// </summary>
public enum TestOutputFormat
{
    /// <summary>Human-readable progress text.</summary>
    Default,

    /// <summary>
    /// A versioned machine-readable document on stdout, with all progress text kept on stderr so a
    /// host can parse stdout even when the command exits non-zero.
    /// </summary>
    Json,

    /// <summary>
    /// JUnit XML on stdout, for CI systems that ingest that format natively. Carries the same case
    /// identities and outcomes as <see cref="Json"/>, with progress text kept on stderr.
    /// </summary>
    JUnit,
}
