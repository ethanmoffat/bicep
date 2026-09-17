// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Bicep.IO.Abstraction;

namespace Bicep.Cli.Services;

/// <summary>
/// Serializes test results as JUnit XML, the format most CI systems ingest natively.
///
/// Like the JSON contract, this carries case identities and outcomes only: never parameter values,
/// template content or any other payload the test was given.
///
/// Two mappings here are deliberate and worth knowing about:
///
/// A case that could not be evaluated is reported as an <c>&lt;error&gt;</c>, not
/// <c>&lt;skipped&gt;</c>. JUnit's "skipped" means a test was intentionally not run, and CI systems
/// treat it as benign. This framework already counts an unevaluated case as a failure of the run, so
/// reporting it as skipped would let a suite whose targets all failed to compile publish as green.
///
/// A suite is one test declaration and a case is one evaluation, so the granularity of the JSON
/// contract is preserved exactly: a declaration matching 250 targets reports 250 test cases, not one.
/// Joining a suite name and a case name with '#' reproduces that case's <c>caseId</c> from the JSON
/// contract, so a host can correlate the two documents without a lookup table.
/// </summary>
public static class TestJUnitSerializer
{
    private const string SuiteName = "bicep test";

    public static string SerializeResults(TestResults results, string? baseDirectory = null)
    {
        var suites = new XElement("testsuites",
            new XAttribute("name", SuiteName),
            new XAttribute("tests", results.TotalEvaluations),
            new XAttribute("failures", results.FailedEvaluations),
            new XAttribute("errors", results.SkippedEvaluations),
            new XAttribute("skipped", 0),
            new XAttribute("time", FormatSeconds(results.TotalDuration)));

        // Grouped by declaration in first-seen order, which is already the stable ordinal order the
        // results were produced in. Re-sorting here would let the two documents disagree.
        foreach (var group in GroupByDeclaration(results.Results))
        {
            suites.Add(CreateSuite(group, baseDirectory));
        }

        return Serialize(new XDocument(suites));
    }

    private static XElement CreateSuite(IGrouping<string, TestResult> group, string? baseDirectory)
    {
        var suite = new XElement("testsuite",
            new XAttribute("name", group.Key),
            new XAttribute("tests", group.Count()),
            new XAttribute("failures", group.Count(x => x.Result.Status is TestCaseStatus.Failed)),
            new XAttribute("errors", group.Count(x => x.Result.Status is TestCaseStatus.Skipped)),
            new XAttribute("skipped", 0),
            new XAttribute("time", FormatSeconds(group.Aggregate(TimeSpan.Zero, (total, x) => total + x.Duration))));

        foreach (var result in group)
        {
            suite.Add(CreateCase(result, group.Key, baseDirectory));
        }

        return suite;
    }

    private static XElement CreateCase(TestResult result, string declaration, string? baseDirectory)
    {
        var testCase = new XElement("testcase",
            new XAttribute("name", GetCaseName(result.Identity)),
            // Repeated from the suite because most parsers group by classname and ignore the suite
            // element's own name.
            new XAttribute("classname", declaration),
            new XAttribute("time", FormatSeconds(result.Duration)));

        // A locator for the target the failure is about, not part of the identity: unlike the name,
        // it is relative to where the command ran, which is the convention every other JUnit producer
        // follows and what lets a host attribute a result to the source tree it came from.
        if (GetFilePath(result.Identity.TargetFile, baseDirectory) is { } filePath)
        {
            testCase.Add(new XAttribute("file", filePath));
        }

        switch (result.Result.Status)
        {
            case TestCaseStatus.Skipped:
                testCase.Add(new XElement("error",
                    new XAttribute("message", $"The target could not be evaluated, so its assertions were never reached."),
                    new XAttribute("type", "EvaluationSkipped"),
                    new XText(result.Result.Error ?? string.Empty)));
                break;

            case TestCaseStatus.Failed:
                testCase.Add(CreateFailure(result.Result));
                break;
        }

        return testCase;
    }

    /// <summary>
    /// One failure element carrying every failed assertion, rather than one element per assertion:
    /// the schema permits several, but many consumers display only the first, which would silently
    /// hide the rest.
    /// </summary>
    private static XElement CreateFailure(TestEvaluation evaluation)
    {
        var failed = evaluation.FailedAssertions;
        var names = string.Join(", ", failed.Select(x => x.Source));
        var body = new List<string>();

        foreach (var assertion in failed)
        {
            body.Add(assertion.Message is { } message
                ? $"{assertion.Source}: {message}"
                : $"{assertion.Source} failed.");

            if (assertion.Error is { } error)
            {
                body.Add($"  Could not be evaluated: {error}");
            }

            // The declarations that broke the rule. A count alone leaves the author to find them.
            foreach (var violation in assertion.Violations)
            {
                body.Add($"  {violation}");
            }
        }

        return new XElement("failure",
            new XAttribute("message", $"{failed.Length} of {evaluation.AllAssertions.Length} assertions failed: {names}"),
            new XAttribute("type", "AssertionFailure"),
            new XText(string.Join("\n", body)));
    }

    private static IEnumerable<IGrouping<string, TestResult>> GroupByDeclaration(ImmutableArray<TestResult> results)
        => results.GroupBy(x => $"{x.Identity.TestFileName}#{x.Identity.TestName}");

    /// <summary>
    /// The part of the case identity below its declaration. Concatenating it to the suite name with
    /// '#' yields exactly the caseId the JSON contract reports.
    /// </summary>
    private static string GetCaseName(TestCaseIdentity identity)
        => identity.Inputs is { } inputs
            ? $"{identity.RelativeTargetPath}#{inputs.InputFileName}#{inputs.Name}"
            : identity.RelativeTargetPath;

    /// <summary>
    /// The target relative to where the command ran, always with '/' separators so the same run
    /// reports the same locator on Windows and Linux. Absent when no relative path can be produced,
    /// because an absolute path would leak the machine's directory layout into a published artifact.
    /// </summary>
    private static string? GetFilePath(IOUri target, string? baseDirectory)
    {
        if (baseDirectory is null || target.TryGetFilePath() is not { } targetPath)
        {
            return null;
        }

        try
        {
            var relative = Path.GetRelativePath(baseDirectory, targetPath);

            return Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Seconds to three decimal places, which is what the format specifies and what the durations in
    /// the JSON contract report in milliseconds. Invariant so a host's locale cannot change the
    /// separator and make the document unparseable.
    /// </summary>
    private static string FormatSeconds(TimeSpan duration)
        => duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(false),
            NewLineChars = "\n",
        };

        using var stringWriter = new EncodingStringWriter();
        using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
        {
            document.Save(xmlWriter);
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Declares utf-8 rather than the utf-16 a string writer would otherwise announce, so the
    /// declaration describes the bytes a host will actually read from the file.
    /// </summary>
    private sealed class EncodingStringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
