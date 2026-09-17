// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Emit;
using Bicep.Core.Intermediate;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TestFramework;
using Bicep.Core.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Evaluates a test's own assertions against the compiler facts of one target.
///
/// The assertion expressions are ordinary Bicep, so they are converted to template-language expressions
/// and evaluated with the target's facts supplied as data. Nothing about the target is deployed, no
/// deployment inputs are required, and no provider is contacted.
/// </summary>
public class TestAssertionEvaluator
{
    /// <summary>
    /// Evaluates every assertion the test declares. An assertion that cannot be evaluated fails with its
    /// own error rather than aborting the ones beside it, so a single broken query never hides the
    /// outcome of the rest of the policy.
    /// </summary>
    public static ImmutableArray<AssertionResult> Evaluate(
        SemanticModel testFileModel,
        TestDeclarationSyntax testDeclaration,
        TestTargetFacts facts,
        TestInputCase? inputs = null)
    {
        if (testDeclaration.TryGetAssertionsSyntax() is not { } assertions)
        {
            return [];
        }

        var context = new EmitterContext(testFileModel);
        var results = ImmutableArray.CreateBuilder<AssertionResult>();

        foreach (var assertion in assertions.Properties)
        {
            if (assertion.TryGetKeyText() is not { } name)
            {
                continue;
            }

            results.Add(EvaluateOne(context, name, assertion.Value as ObjectSyntax, facts, inputs));
        }

        return results.ToImmutable();
    }

    private static AssertionResult EvaluateOne(EmitterContext context, string name, ObjectSyntax? body, TestTargetFacts facts, TestInputCase? inputs)
    {
        var messageSyntax = body?.TryGetPropertyByName(TestAssertion.MessagePropertyName)?.Value;
        var passWhen = body?.TryGetPropertyByName(TestAssertion.PassWhenPropertyName)?.Value;
        var failOn = body?.TryGetPropertyByName(TestAssertion.FailOnPropertyName)?.Value;
        var message = ResolveMessage(context, messageSyntax, facts, inputs);

        try
        {
            if (passWhen is not null)
            {
                var value = EvaluateExpression(context, passWhen, facts, "bool", inputs);

                return new AssertionResult(name, value.Type == JTokenType.Boolean && value.Value<bool>())
                {
                    Message = message,
                };
            }

            if (failOn is not null)
            {
                var value = EvaluateExpression(context, failOn, facts, "array", inputs);
                var violations = value is JArray array ? array : [];

                return new AssertionResult(name, violations.Count == 0)
                {
                    Message = message,
                    Violations = [.. violations.Select(DescribeViolation)],
                };
            }

            // The compiler already reported this; fail rather than silently passing an unjudgeable assertion.
            return new AssertionResult(name, false) { Message = message, Error = "The assertion declares neither a condition nor an offending-fact collection." };
        }
        catch (Exception exception)
        {
            return new AssertionResult(name, false) { Message = message, Error = BicepValueEvaluator.Sanitize(exception) };
        }
    }

    /// <summary>
    /// Resolves the assertion's message. A message is ordinary Bicep, so it may interpolate the case
    /// values the assertion ran with; an author who states a threshold in the message should not have
    /// to keep it in step with the condition by hand. A message that cannot be evaluated is dropped
    /// rather than being allowed to decide the assertion's outcome.
    /// </summary>
    private static string? ResolveMessage(EmitterContext context, SyntaxBase? syntax, TestTargetFacts facts, TestInputCase? inputs)
    {
        if (syntax is null)
        {
            return null;
        }

        if (syntax is StringSyntax { } stringSyntax && stringSyntax.TryGetLiteralValue() is { } literal)
        {
            return literal;
        }

        try
        {
            return EvaluateExpression(context, syntax, facts, "string", inputs).Value<string>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Evaluates one assertion expression with the target facts in scope, along with the variables and
    /// case inputs the test file contributes to that expression.
    /// </summary>
    private static JToken EvaluateExpression(EmitterContext context, SyntaxBase syntax, TestTargetFacts facts, string outputType, TestInputCase? inputs)
    {
        var seed = new JObject
        {
            [TestAssertion.TargetVariableName] = TestTargetFactsSerializer.Serialize(facts),
        };

        return BicepValueEvaluator.Evaluate(context, syntax, outputType, seed, inputs?.Values);
    }

    /// <summary>
    /// Renders one offending fact as a source location. Facts carry their declaring file and line, so a
    /// failure names the declarations that violated the policy rather than only how many there were.
    /// </summary>
    private static string DescribeViolation(JToken violation)
    {
        if (violation is not JObject fact)
        {
            return violation.ToString(Formatting.None);
        }

        var file = fact[TestTargetType.FilePropertyName]?.Value<string>();
        var line = fact[TestTargetType.LinePropertyName]?.Value<int>();
        var name = fact[TestTargetType.NamePropertyName]?.Value<string>()
            ?? fact[TestTargetType.PathPropertyName]?.Value<string>();

        if (file is null || line is null)
        {
            return fact.ToString(Formatting.None);
        }

        return name is null ? $"{file}({line})" : $"{file}({line}): {name}";
    }

}
