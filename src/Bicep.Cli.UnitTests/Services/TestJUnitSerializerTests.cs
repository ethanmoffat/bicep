// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Xml.Linq;
using Bicep.Cli.Services;
using Bicep.IO.Abstraction;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestResult = Bicep.Cli.Services.TestResult;

namespace Bicep.Cli.UnitTests.Services;

[TestClass]
public class TestJUnitSerializerTests
{
    private static TestCaseIdentity Identity(string testFile, string testName, string targetFile)
        => new(IOUri.FromFilePath(testFile), testName, IOUri.FromFilePath(targetFile));

    private static TestResult Result(TestCaseIdentity identity, TestEvaluation evaluation, TimeSpan duration = default)
        => new(null!, identity, evaluation) { Duration = duration };

    private static TestEvaluation Passed(params string[] assertions)
        => new(null, null, [.. assertions.Select(name => new AssertionResult(name, true))], []);

    private static TestEvaluation Failed(string passing, AssertionResult failing)
        => new(null, null, [new AssertionResult(passing, true), failing], [failing]);

    private static TestEvaluation Errored(string error) => new(null, error, [], []);

    private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

    private static IEnumerable<XElement> Cases(XElement root) => root.Descendants("testcase");

    [TestMethod]
    public void SerializeResults_ReportsOneSuitePerDeclarationAndOneCasePerEvaluation()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a")),
            Result(Identity("/repo/main.biceptest", "policy", "/repo/two.bicep"), Passed("a")),
            Result(Identity("/repo/main.biceptest", "otherPolicy", "/repo/one.bicep"), Passed("a")),
        ])));

        var suites = root.Elements("testsuite").ToArray();

        // A declaration matching many targets keeps one case per target: collapsing them would
        // discard exactly the attribution that makes a source policy actionable.
        suites.Should().HaveCount(2);
        suites[0].Attribute("name")!.Value.Should().Be("main.biceptest#policy");
        suites[0].Elements("testcase").Should().HaveCount(2);
        suites[1].Attribute("name")!.Value.Should().Be("main.biceptest#otherPolicy");
        suites[1].Elements("testcase").Should().HaveCount(1);

        root.Attribute("tests")!.Value.Should().Be("3");
    }

    [TestMethod]
    public void SerializeResults_JoiningSuiteAndCaseNamesReproducesTheJsonCaseId()
    {
        var identity = Identity("/repo/main.biceptest", "policy", "/repo/modules/one.bicep");
        var root = Parse(TestJUnitSerializer.SerializeResults(new([Result(identity, Passed("a"))])));

        var suite = root.Elements("testsuite").Single();
        var testCase = suite.Elements("testcase").Single();

        // The two documents describe the same cases, so a host must be able to correlate them
        // without a lookup table.
        $"{suite.Attribute("name")!.Value}#{testCase.Attribute("name")!.Value}"
            .Should().Be(identity.CaseId);
    }

    [TestMethod]
    public void SerializeResults_NamesTheInputCaseSoRunsWithDifferentValuesAreDistinct()
    {
        var identity = new TestCaseIdentity(
            IOUri.FromFilePath("/repo/main.biceptest"),
            "policy",
            IOUri.FromFilePath("/repo/one.bicep"),
            new TestInputCase(IOUri.FromFilePath("/repo/main.biceptestparam"), "small", [], TestDeploymentContext.Empty));

        var root = Parse(TestJUnitSerializer.SerializeResults(new([Result(identity, Passed("a"))])));
        var testCase = Cases(root).Single();

        testCase.Attribute("name")!.Value.Should().Be("one.bicep#main.biceptestparam#small");
        $"{testCase.Attribute("classname")!.Value}#{testCase.Attribute("name")!.Value}"
            .Should().Be(identity.CaseId);
    }

    [TestMethod]
    public void SerializeResults_ReportsAnUnevaluatedTargetAsAnErrorRatherThanSkipped()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Errored("Missing parameter 'prefix'.")),
        ])));

        var testCase = Cases(root).Single();

        // JUnit's "skipped" means a test was deliberately not run, and CI systems treat it as benign.
        // This framework counts an unevaluated case as a failure of the run, so reporting it as
        // skipped would let a suite whose targets all failed to compile publish as green.
        testCase.Element("skipped").Should().BeNull();
        testCase.Element("error").Should().NotBeNull();
        testCase.Element("error")!.Attribute("type")!.Value.Should().Be("EvaluationError");
        testCase.Element("error")!.Value.Should().Contain("Missing parameter 'prefix'.");

        root.Attribute("errors")!.Value.Should().Be("1");
        root.Attribute("skipped")!.Value.Should().Be("0");
    }

    [TestMethod]
    public void SerializeResults_NamesTheOffendingDeclarationsInTheFailureBody()
    {
        var failing = new AssertionResult("noStorageAccounts", false)
        {
            Message = "Storage accounts are owned by the platform team.",
            Violations = ["one.bicep(12): storageAccount", "one.bicep(20): otherAccount"],
        };

        var root = Parse(TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Failed("a", failing)),
        ])));

        var failure = Cases(root).Single().Element("failure")!;

        failure.Attribute("message")!.Value.Should().Be("1 of 2 assertions failed: noStorageAccounts");
        failure.Value.Should().Contain("noStorageAccounts: Storage accounts are owned by the platform team.");
        failure.Value.Should().Contain("one.bicep(12): storageAccount");
        failure.Value.Should().Contain("one.bicep(20): otherAccount");
    }

    [TestMethod]
    public void SerializeResults_ReportsAnAssertionThatCouldNotBeEvaluated()
    {
        var failing = new AssertionResult("namingRule", false)
        {
            Message = "Names must be prefixed.",
            Error = "The property 'name' was not available.",
        };

        var root = Parse(TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Failed("a", failing)),
        ])));

        // An assertion that broke is not the same as an assertion that found a violation, and the
        // body has to say which happened or the author cannot tell them apart.
        Cases(root).Single().Element("failure")!.Value
            .Should().Contain("Could not be evaluated: The property 'name' was not available.");
    }

    [TestMethod]
    public void SerializeResults_ReportsDurationsInSeconds()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a"), TimeSpan.FromMilliseconds(1250)),
            Result(Identity("/repo/main.biceptest", "policy", "/repo/two.bicep"), Passed("a"), TimeSpan.FromMilliseconds(500)),
        ])));

        var cases = Cases(root).ToArray();

        // JUnit's time attribute is seconds, while the JSON contract reports milliseconds.
        cases[0].Attribute("time")!.Value.Should().Be("1.250");
        cases[1].Attribute("time")!.Value.Should().Be("0.500");
        root.Elements("testsuite").Single().Attribute("time")!.Value.Should().Be("1.750");
        root.Attribute("time")!.Value.Should().Be("1.750");
    }

    [TestMethod]
    public void SerializeResults_LocatesTheTargetRelativeToWhereTheCommandRan()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(
            new([Result(Identity("/repo/tests/main.biceptest", "policy", "/repo/src/one.bicep"), Passed("a"))]),
            IOUri.FromFilePath("/repo/").GetFilePath()));

        // Always '/' separated, so the same run reports the same locator on Windows and Linux.
        Cases(root).Single().Attribute("file")!.Value.Should().Be("src/one.bicep");
    }

    [TestMethod]
    public void SerializeResults_OmitsTheLocatorWhenNoRelativePathExists()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(
            new([Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a"))])));

        // An absolute path would leak the machine's directory layout into a published artifact.
        Cases(root).Single().Attribute("file").Should().BeNull();
    }

    [TestMethod]
    public void SerializeResults_EscapesContentThatWouldOtherwiseBreakTheDocument()
    {
        var failing = new AssertionResult("rule<&>", false)
        {
            Message = "Use \"quotes\" & <angles>.",
            Violations = ["one.bicep(1): a & b"],
        };

        var xml = TestJUnitSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy<&>", "/repo/one.bicep"), Failed("a", failing)),
        ]));

        // Parsing is the assertion: an unescaped message would make the whole report unreadable,
        // which a host would see as a broken run rather than as the failure it actually was.
        var failure = Cases(Parse(xml)).Single().Element("failure")!;

        failure.Value.Should().Contain("Use \"quotes\" & <angles>.");
        failure.Value.Should().Contain("one.bicep(1): a & b");
        Parse(xml).Elements("testsuite").Single().Attribute("name")!.Value.Should().Be("main.biceptest#policy<&>");
    }

    [TestMethod]
    public void SerializeResults_ProducesAValidDocumentForAnEmptyRun()
    {
        var root = Parse(TestJUnitSerializer.SerializeResults(new([])));

        root.Name.LocalName.Should().Be("testsuites");
        root.Attribute("tests")!.Value.Should().Be("0");
        root.Elements("testsuite").Should().BeEmpty();
    }

    [TestMethod]
    public void SerializeResults_DeclaresTheEncodingAHostWillActuallyRead()
    {
        var xml = TestJUnitSerializer.SerializeResults(new([]));

        // A string writer announces utf-16 by default, which would misdescribe the bytes written
        // to a results file and make some parsers reject it outright.
        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    }
}
