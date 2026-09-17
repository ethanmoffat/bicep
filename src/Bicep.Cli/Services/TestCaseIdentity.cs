// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.IO.Abstraction;

namespace Bicep.Cli.Services;

/// <summary>
/// The immutable identity of a single test case: one declared test bound to one target file.
/// A test that selects multiple targets produces one identity per target.
/// </summary>
public record TestCaseIdentity(IOUri TestFile, string TestName, IOUri TargetFile, TestInputCase? Inputs = null) : IComparable<TestCaseIdentity>
{
    /// <summary>
    /// The file name of the test file, without any directory portion.
    /// </summary>
    public string TestFileName => GetFileName(TestFile);

    /// <summary>
    /// The target path relative to the directory containing the test file, always using '/' separators.
    /// Relative to the test file rather than the working directory so that the identity does not change
    /// when the same test is run from a different directory.
    /// </summary>
    public string RelativeTargetPath => TargetFile.GetPathRelativeTo(TestFile);

    /// <summary>
    /// Whether this identity describes the test itself rather than a resolved target. This happens when
    /// target selection failed, so there is no target to attribute the outcome to.
    /// </summary>
    public bool IsSelfTargeted => TargetFile == TestFile;

    /// <summary>
    /// A stable, human-readable identifier for this case.
    /// It is derived only from test-file-relative information, so it is unaffected by the
    /// working directory the CLI was invoked from.
    /// </summary>
    public string CaseId => Inputs is { } inputs
        ? $"{TestFileName}#{TestName}#{RelativeTargetPath}#{inputs.InputFileName}#{inputs.Name}"
        : $"{TestFileName}#{TestName}#{RelativeTargetPath}";

    public int CompareTo(TestCaseIdentity? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (ComparePaths(TestFile, other.TestFile) is var testFileComparison and not 0)
        {
            return testFileComparison;
        }

        if (string.CompareOrdinal(TestName, other.TestName) is var testNameComparison and not 0)
        {
            return testNameComparison;
        }

        if (ComparePaths(TargetFile, other.TargetFile) is var targetComparison and not 0)
        {
            return targetComparison;
        }

        return string.CompareOrdinal(Inputs?.Name, other.Inputs?.Name);
    }

    /// <summary>
    /// Orders two paths identically on every host.
    /// </summary>
    /// <remarks>
    /// Reported order is part of the result contract, so it deliberately does not use the host's
    /// case rules: a suite must not list its cases in one order on Windows and another on Linux.
    /// Paths the host considers the same file still compare equal, keeping this consistent with
    /// the structural equality of the record.
    /// </remarks>
    private static int ComparePaths(IOUri left, IOUri right)
        => left.Equals(right) ? 0 : string.CompareOrdinal(left.ToString(), right.ToString());

    private static string GetFileName(IOUri uri)
    {
        var segments = uri.PathSegments;

        return segments.Length > 0 ? segments[^1] : uri.Path;
    }
}
