// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Bicep.Cli.Services;
using Bicep.IO.Abstraction;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestResult = Bicep.Cli.Services.TestResult;

namespace Bicep.Cli.UnitTests.Services;

[TestClass]
public class TestReportSerializerTests
{
    private static TestCaseIdentity Identity(string testFile, string testName, string targetFile)
        => new(IOUri.FromFilePath(testFile), testName, IOUri.FromFilePath(targetFile));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static TestResult Result(TestCaseIdentity identity, TestEvaluation evaluation)
        => new(null!, identity, evaluation);

    private static TestResult Result(TestCaseIdentity identity, TestEvaluation evaluation, TimeSpan duration)
        => new(null!, identity, evaluation) { Duration = duration };

    private static TestEvaluation Passed(params string[] assertions)
        => new(null, null, [.. assertions.Select(name => new AssertionResult(name, true))], []);

    private static TestEvaluation Failed(string passing, string failing)
        => new(
            null,
            null,
            [new AssertionResult(passing, true), new AssertionResult(failing, false)],
            [new AssertionResult(failing, false)]);

    private static TestEvaluation Errored(string error) => new(null, error, [], []);

    [TestMethod]
    public void SerializeInventory_ReportsVersionModeAndCaseIdentities()
    {
        var json = TestReportSerializer.SerializeInventory(
        [
            new(Identity("/repo/main.biceptest", "policy", "/repo/modules/one.bicep"), null),
        ]);

        var root = Parse(json);

        root.GetProperty("version").GetString().Should().Be(TestReportSerializer.ContractVersion);
        root.GetProperty("mode").GetString().Should().Be("list");

        var single = root.GetProperty("cases").EnumerateArray().Single();
        single.GetProperty("caseId").GetString().Should().Be("main.biceptest#policy#modules/one.bicep");
        single.GetProperty("testFile").GetString().Should().Be("main.biceptest");
        single.GetProperty("testName").GetString().Should().Be("policy");
        single.GetProperty("target").GetString().Should().Be("modules/one.bicep");
        single.GetProperty("status").GetString().Should().Be("listed");
        single.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);

        // Listing reports inventory only. It must never carry assertion outcomes.
        single.TryGetProperty("assertions", out _).Should().BeFalse();
    }

    [TestMethod]
    public void SerializeInventory_ReportsUnresolvedTestsWithoutATarget()
    {
        var json = TestReportSerializer.SerializeInventory(
        [
            new(Identity("/repo/main.biceptest", "policy", "/repo/main.biceptest"), "The selector matched no files."),
        ]);

        var root = Parse(json);
        var single = root.GetProperty("cases").EnumerateArray().Single();

        single.GetProperty("status").GetString().Should().Be("unresolved");
        single.GetProperty("error").GetString().Should().Be("The selector matched no files.");

        // A test that resolved to nothing has no target to name, and must not invent one.
        single.GetProperty("target").ValueKind.Should().Be(JsonValueKind.Null);

        root.GetProperty("summary").GetProperty("unresolved").GetInt32().Should().Be(1);
        root.GetProperty("summary").GetProperty("listed").GetInt32().Should().Be(0);
    }

    [TestMethod]
    public void SerializeResults_ReportsStatusAssertionCountsAndSummary()
    {
        var json = TestReportSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a", "b")),
            Result(Identity("/repo/main.biceptest", "policy", "/repo/two.bicep"), Failed("a", "b")),
            Result(Identity("/repo/main.biceptest", "policy", "/repo/three.bicep"), Errored("Missing parameter.")),
        ]));

        var root = Parse(json);

        root.GetProperty("mode").GetString().Should().Be("run");

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();

        cases[0].GetProperty("status").GetString().Should().Be("passed");
        cases[0].GetProperty("assertions").GetProperty("total").GetInt32().Should().Be(2);
        cases[0].GetProperty("assertions").GetProperty("failed").GetInt32().Should().Be(0);

        cases[1].GetProperty("status").GetString().Should().Be("failed");
        cases[1].GetProperty("assertions").GetProperty("failed").GetInt32().Should().Be(1);
        cases[1].GetProperty("assertions").GetProperty("failedNames").EnumerateArray()
            .Select(x => x.GetString()).Should().Equal("b");

        // An evaluation that never ran has no assertion counts to report; reporting zeroes would
        // be indistinguishable from a target that genuinely declares no assertions.
        cases[2].GetProperty("status").GetString().Should().Be("errored");
        cases[2].GetProperty("error").GetString().Should().Be("Missing parameter.");
        cases[2].TryGetProperty("assertions", out _).Should().BeFalse();

        var summary = root.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(3);
        summary.GetProperty("passed").GetInt32().Should().Be(1);
        summary.GetProperty("failed").GetInt32().Should().Be(1);
        summary.GetProperty("errored").GetInt32().Should().Be(1);
    }

    [TestMethod]
    public void SerializeResults_ReportsPerCaseAndTotalDurations()
    {
        var json = TestReportSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a"), TimeSpan.FromMilliseconds(12.5)),
            Result(Identity("/repo/main.biceptest", "policy", "/repo/two.bicep"), Failed("a", "b"), TimeSpan.FromMilliseconds(7.25)),
            // A target that could not be evaluated still cost time to reject, so it is reported too.
            Result(Identity("/repo/main.biceptest", "policy", "/repo/three.bicep"), Errored("Missing parameter."), TimeSpan.FromMilliseconds(3)),
        ]));

        var root = Parse(json);
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();

        cases[0].GetProperty("durationMs").GetDouble().Should().Be(12.5);
        cases[1].GetProperty("durationMs").GetDouble().Should().Be(7.25);
        cases[2].GetProperty("durationMs").GetDouble().Should().Be(3);

        root.GetProperty("summary").GetProperty("durationMs").GetDouble().Should().Be(22.75);
    }

    [TestMethod]
    public void SerializeResults_RoundsDurationsRatherThanReportingTickNoise()
    {
        var json = TestReportSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/main.biceptest", "policy", "/repo/one.bicep"), Passed("a"), TimeSpan.FromTicks(1234567)),
        ]));

        var single = Parse(json).GetProperty("cases").EnumerateArray().Single();

        // 1234567 ticks is 123.4567ms; three decimal places is finer than any host reports.
        single.GetProperty("durationMs").GetDouble().Should().Be(123.457);
    }

    [TestMethod]
    public void SerializeResults_IdentitiesAreFreeOfAbsolutePaths()
    {
        var json = TestReportSerializer.SerializeResults(new(
        [
            Result(Identity("/repo/tests/main.biceptest", "policy", "/repo/tests/one.bicep"), Passed("a")),
        ]));

        // Case identities are derived from test-file-relative information so that a host sees the
        // same identity regardless of where the CLI was invoked from.
        json.Should().NotContain("/repo");
    }

    [TestMethod]
    public void SerializeResults_ProducesValidJsonForAnEmptyRun()
    {
        var json = TestReportSerializer.SerializeResults(new([]));

        var root = Parse(json);

        root.GetProperty("cases").GetArrayLength().Should().Be(0);
        root.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(0);
    }
}
