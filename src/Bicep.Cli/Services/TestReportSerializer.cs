// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bicep.Cli.Services;

/// <summary>
/// Serializes test inventory and test results into a versioned machine-readable document.
///
/// This is the contract a host process parses. It carries case identities and outcomes only:
/// never parameter values, template content or any other payload the test was given. Human
/// progress text is written separately and is never mixed into this document.
/// </summary>
public static class TestReportSerializer
{
    /// <summary>
    /// The contract version. Increment when the shape changes in a way a host must react to.
    /// Additive, optional properties do not require a new version.
    ///
    /// 1.1 renamed the "skipped" status and summary count to "errored". The old name said a case had
    /// been deliberately left out, when in fact it had been attempted and could not be evaluated.
    /// </summary>
    public const string ContractVersion = "1.1";
    private const string ListedStatus = "listed";
    private const string UnresolvedStatus = "unresolved";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string SerializeInventory(IEnumerable<TestInventoryEntry> entries)
    {
        var cases = new JsonArray();
        var listed = 0;
        var unresolved = 0;

        foreach (var entry in entries)
        {
            var status = entry.IsResolved ? ListedStatus : UnresolvedStatus;

            if (entry.IsResolved)
            {
                listed++;
            }
            else
            {
                unresolved++;
            }

            cases.Add((JsonNode)CreateCase(entry.Identity, status, entry.Error));
        }

        return Serialize(new JsonObject
        {
            ["version"] = ContractVersion,
            ["mode"] = "list",
            ["cases"] = cases,
            ["summary"] = new JsonObject
            {
                ["total"] = listed + unresolved,
                ["listed"] = listed,
                ["unresolved"] = unresolved,
            },
        });
    }

    public static string SerializeResults(TestResults results)
    {
        var cases = new JsonArray();

        foreach (var result in results.Results)
        {
            var node = CreateCase(result.Identity, GetStatusName(result.Result.Status), result.Result.Error);

            // How long this case cost. Reported for errored cases too: deciding a target cannot be
            // evaluated still takes time, and a host that hides that cost cannot explain its own runtime.
            node["durationMs"] = RoundToMilliseconds(result.Duration);

            // Assertion counts are only meaningful when the target was actually evaluated.
            if (result.Result.Status is not TestCaseStatus.Errored)
            {
                node["assertions"] = new JsonObject
                {
                    ["total"] = result.Result.AllAssertions.Length,
                    ["failed"] = result.Result.FailedAssertions.Length,
                    ["failedNames"] = CreateNames(result.Result.FailedAssertions),
                    ["failures"] = CreateFailures(result.Result.FailedAssertions),
                };
            }

            cases.Add((JsonNode)node);
        }

        return Serialize(new JsonObject
        {
            ["version"] = ContractVersion,
            ["mode"] = "run",
            ["cases"] = cases,
            ["summary"] = new JsonObject
            {
                ["total"] = results.TotalEvaluations,
                ["passed"] = results.SuccessfulEvaluations,
                ["failed"] = results.FailedEvaluations,
                ["errored"] = results.ErroredEvaluations,
                ["durationMs"] = RoundToMilliseconds(results.TotalDuration),
            },
        });
    }

    /// <summary>
    /// Milliseconds to three decimal places. Serializing the raw tick count would make the document
    /// differ on every run for no reader's benefit, and microseconds are already finer than anything
    /// a host reports.
    /// </summary>
    private static double RoundToMilliseconds(TimeSpan duration) => Math.Round(duration.TotalMilliseconds, 3);

    private static JsonObject CreateCase(TestCaseIdentity identity, string status, string? error)
        => new()
        {
            ["caseId"] = identity.CaseId,
            ["testFile"] = identity.TestFileName,
            ["testName"] = identity.TestName,
            // A case with no resolved target is attributed to the test itself rather than to a
            // target that does not exist.
            ["target"] = identity.IsSelfTargeted ? null : identity.RelativeTargetPath,
            // Present only when the run supplied input cases, so an existing host that never passes
            // --inputs sees exactly the document it saw before.
            ["inputFile"] = identity.Inputs?.InputFileName,
            ["inputCase"] = identity.Inputs?.Name,
            ["status"] = status,
            ["error"] = error,
        };

    private static JsonArray CreateNames(ImmutableArray<AssertionResult> assertions)
    {
        var names = new JsonArray();

        foreach (var assertion in assertions)
        {
            names.Add((JsonNode?)JsonValue.Create(assertion.Source));
        }

        return names;
    }

    /// <summary>
    /// Describes each failure in enough detail for a host to act on it without reparsing console text.
    /// Violations are source locations drawn from the target's own declarations, never fact payloads.
    /// </summary>
    private static JsonArray CreateFailures(ImmutableArray<AssertionResult> assertions)
    {
        var failures = new JsonArray();

        foreach (var assertion in assertions)
        {
            var violations = new JsonArray();

            foreach (var violation in assertion.Violations)
            {
                violations.Add((JsonNode?)JsonValue.Create(violation));
            }

            failures.Add((JsonNode)new JsonObject
            {
                ["name"] = assertion.Source,
                ["message"] = assertion.Message,
                ["error"] = assertion.Error,
                ["violations"] = violations,
            });
        }

        return failures;
    }

    private static string GetStatusName(TestCaseStatus status)
        => status switch
        {
            TestCaseStatus.Passed => "passed",
            TestCaseStatus.Failed => "failed",
            TestCaseStatus.Errored => "errored",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static string Serialize(JsonObject document) => document.ToJsonString(SerializerOptions);
}
