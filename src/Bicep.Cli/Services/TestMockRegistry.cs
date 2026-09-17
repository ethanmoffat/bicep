// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.TestFramework;
using Bicep.Core.Utils;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// One runtime read a test answers, and the answer it gives.
/// </summary>
public record TestMock(
    string Name,
    string Operation,
    string ResourceId,
    string ApiVersion,
    JToken? RequestBody,
    JToken? Response);

/// <summary>
/// Raised when evaluation reaches a runtime read whose configuration cannot produce an answer. The
/// message names the mock and the request, never the response, because a synthetic response can carry
/// data that reads like a secret even when it is invented.
/// </summary>
public class TestMockException(string message) : Exception(message);

/// <summary>
/// The mocks in effect for one input case.
///
/// Matching is exact on operation, resource ID, API version and request body. A mock is an identity,
/// not a pattern: a wildcard would answer calls the author never meant to make, and a test that passes
/// because of an unintended answer is worse than one that fails.
/// </summary>
public class TestMockRegistry
{
    public static readonly TestMockRegistry Empty = new([]);

    private readonly ImmutableArray<TestMock> mocks;

    private TestMockRegistry(ImmutableArray<TestMock> mocks)
    {
        this.mocks = mocks;
    }

    public bool IsEmpty => this.mocks.IsEmpty;

    /// <summary>
    /// Reads the evaluated 'mocks' object of a test file. The values have already been computed as
    /// ordinary Bicep for this case, so a mock may be built from the case's own inputs.
    /// </summary>
    public static TestMockRegistry FromObject(JObject? source)
    {
        if (source is null)
        {
            return Empty;
        }

        var builder = ImmutableArray.CreateBuilder<TestMock>();

        foreach (var property in source.Properties())
        {
            if (property.Value is not JObject entry)
            {
                throw new TestMockException($"Mock \"{property.Name}\" is not an object.");
            }

            var operation = RequiredString(property.Name, entry, TestMockType.OperationPropertyName);
            var resourceId = RequiredString(property.Name, entry, TestMockType.ResourceIdPropertyName);
            var apiVersion = RequiredString(property.Name, entry, TestMockType.ApiVersionPropertyName);

            if (!IsSupportedOperation(operation))
            {
                throw new TestMockException($"Mock \"{property.Name}\" declares unsupported operation \"{operation}\".");
            }

            builder.Add(new TestMock(
                property.Name,
                operation,
                Normalize(resourceId),
                apiVersion,
                entry[TestMockType.RequestBodyPropertyName],
                entry[TestMockType.ResponsePropertyName]));
        }

        var registry = new TestMockRegistry(builder.ToImmutable());

        registry.RejectDuplicates();

        return registry;
    }

    /// <summary>
    /// Answers a 'reference' call, or returns null to leave the evaluator's own resolution in place.
    /// </summary>
    public JToken? TryReference(string resourceId, string apiVersion, bool fullBody)
    {
        if (Match(TestMockType.ReferenceOperation, resourceId, apiVersion, requestBody: null) is not { } mock)
        {
            return null;
        }

        if (mock.Response is not JObject envelope)
        {
            throw new TestMockException($"Mock \"{mock.Name}\" matched a reference to {Describe(resourceId, apiVersion)} but declares no response.");
        }

        if (fullBody)
        {
            return envelope;
        }

        // An ordinary reference reads the properties view, so a response that never declares one cannot
        // answer the call that was actually made.
        if (envelope[TestMockType.ResponsePropertiesName] is not { } properties)
        {
            throw new TestMockException($"Mock \"{mock.Name}\" matched a reference to {Describe(resourceId, apiVersion)} but its response declares no \"{TestMockType.ResponsePropertiesName}\".");
        }

        return properties;
    }

    /// <summary>
    /// Answers a 'list*' call, or returns null to leave the evaluator's own resolution in place.
    /// </summary>
    public JToken? TryList(string operation, string resourceId, string apiVersion, JToken? requestBody)
    {
        if (Match(operation, resourceId, apiVersion, requestBody) is not { } mock)
        {
            return null;
        }

        if (mock.Response is not { } response)
        {
            throw new TestMockException($"Mock \"{mock.Name}\" matched {operation} on {Describe(resourceId, apiVersion)} but declares no response.");
        }

        // A list result is the operation's own result, not a resource envelope, so it is used as written.
        return response;
    }

    /// <summary>
    /// The API version a mock would answer this resource with, when the caller did not state one.
    /// Ambiguity is not resolved by guessing: two versions for one resource mean the request cannot be
    /// attributed to a single setup.
    /// </summary>
    public string? TryResolveApiVersion(string operation, string resourceId)
    {
        var normalized = Normalize(resourceId);

        var candidates = this.mocks
            .Where(mock => mock.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase) &&
                           mock.ResourceId.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(mock => mock.ApiVersion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private TestMock? Match(string operation, string resourceId, string apiVersion, JToken? requestBody)
    {
        var normalized = Normalize(resourceId);

        var matches = this.mocks
            .Where(mock => mock.Operation.Equals(operation, StringComparison.OrdinalIgnoreCase) &&
                           mock.ResourceId.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                           mock.ApiVersion.Equals(apiVersion, StringComparison.OrdinalIgnoreCase) &&
                           BodyMatches(mock.RequestBody, requestBody))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new TestMockException(
                $"Mocks {string.Join(", ", matches.Select(match => $"\"{match.Name}\"").Order(StringComparer.Ordinal))} all match {operation} on {Describe(resourceId, apiVersion)}. Remove the duplicates so the request has one answer.");
        }

        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Two entries that answer the same request are a configuration error even if the request is never
    /// made, because there is no rule that could choose between them later.
    /// </summary>
    private void RejectDuplicates()
    {
        foreach (var group in this.mocks.GroupBy(mock => (mock.Operation.ToLowerInvariant(), mock.ResourceId.ToLowerInvariant(), mock.ApiVersion.ToLowerInvariant())))
        {
            var duplicates = group
                .Where(mock => group.Any(other => !ReferenceEquals(other, mock) && BodyMatches(other.RequestBody, mock.RequestBody)))
                .ToArray();

            if (duplicates.Length > 0)
            {
                throw new TestMockException(
                    $"Mocks {string.Join(", ", duplicates.Select(duplicate => $"\"{duplicate.Name}\"").Order(StringComparer.Ordinal))} answer the same request. Remove the duplicates so the request has one answer.");
            }
        }
    }

    /// <summary>
    /// An absent body is not a wildcard: it matches only a request that has no body.
    /// </summary>
    private static bool BodyMatches(JToken? configured, JToken? actual)
        => (configured, actual) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            _ => JToken.DeepEquals(configured, actual),
        };

    private static bool IsSupportedOperation(string operation)
        => operation.Equals(TestMockType.ReferenceOperation, StringComparison.OrdinalIgnoreCase) ||
           operation.Equals(TestMockType.ListKeysOperation, StringComparison.OrdinalIgnoreCase);

    private static string RequiredString(string name, JObject entry, string property)
        => entry[property]?.Value<string>() is { Length: > 0 } value
            ? value
            : throw new TestMockException($"Mock \"{name}\" is missing \"{property}\".");

    /// <summary>
    /// Resource IDs differ only in case and trailing separators, so those are the only differences
    /// normalized away. Nothing about the identity itself is relaxed.
    /// </summary>
    private static string Normalize(string resourceId) => resourceId.TrimEnd('/');

    /// <summary>
    /// Reports a runtime read that nothing answered. The request is named; a test cannot proceed past a
    /// value it was never given, and guessing one would make the result mean nothing.
    /// </summary>
    public static void ReportUnanswered(string operation, string resourceId, string? apiVersion)
        => throw new TestMockException(
            $"No mock answers {operation} on {Describe(resourceId, apiVersion ?? "no api version")}. Declare it in the test file's 'mocks' so the value the deployment reads is stated by the test.");

    private static string Describe(string resourceId, string apiVersion) => $"{resourceId} ({apiVersion})";
}

/// <summary>
/// Attaches a case's mocks to an evaluation.
/// </summary>
public static class TestMockRegistryExtensions
{
    public static TemplateEvaluator.EvaluationConfiguration Apply(TestMockRegistry? mocks, TemplateEvaluator.EvaluationConfiguration configuration)
    {
        var registry = mocks ?? TestMockRegistry.Empty;

        return configuration with
        {
            OnMockedRequestFunc = registry.IsEmpty
                ? null
                : (operation, resourceId, apiVersion, requestBody, fullBody) =>
                    operation.Equals(TestMockType.ReferenceOperation, StringComparison.OrdinalIgnoreCase)
                        ? registry.TryReference(resourceId, apiVersion, fullBody)
                        : registry.TryList(operation, resourceId, apiVersion, requestBody),
            ResolveMockedApiVersionFunc = registry.IsEmpty ? null : registry.TryResolveApiVersion,
            OnUnansweredRequestFunc = TestMockRegistry.ReportUnanswered,
        };
    }
}
