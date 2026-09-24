// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Emit;
using Bicep.Core.Intermediate;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Syntax.Visitors;
using Bicep.Core.TestFramework;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;
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
        TestInputCase? inputs = null,
        TestEvaluatedFactsProvider? evaluated = null)
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

            results.Add(EvaluateOne(context, name, assertion.Value as ObjectSyntax, facts, inputs, evaluated));
        }

        return results.ToImmutable();
    }

    private static AssertionResult EvaluateOne(EmitterContext context, string name, ObjectSyntax? body, TestTargetFacts facts, TestInputCase? inputs, TestEvaluatedFactsProvider? evaluated)
    {
        var messageSyntax = body?.TryGetPropertyByName(TestAssertion.MessagePropertyName)?.Value;
        var passWhen = body?.TryGetPropertyByName(TestAssertion.PassWhenPropertyName)?.Value;
        var failOn = body?.TryGetPropertyByName(TestAssertion.FailOnPropertyName)?.Value;
        var message = ResolveMessage(context, messageSyntax, facts, inputs, evaluated);

        try
        {
            if (passWhen is not null)
            {
                var value = EvaluateExpression(context, passWhen, facts, "bool", inputs, evaluated);

                return new AssertionResult(name, value.Type == JTokenType.Boolean && value.Value<bool>())
                {
                    Message = message,
                };
            }

            if (failOn is not null)
            {
                var value = EvaluateExpression(context, failOn, facts, "array", inputs, evaluated);
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
    private static string? ResolveMessage(EmitterContext context, SyntaxBase? syntax, TestTargetFacts facts, TestInputCase? inputs, TestEvaluatedFactsProvider? evaluated)
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
            return EvaluateExpression(context, syntax, facts, "string", inputs, evaluated).Value<string>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Evaluates one assertion expression with the target facts in scope, along with the variables and
    /// case inputs the test file contributes to that expression.
    ///
    /// Evaluated values are supplied only to expressions that ask for them. Reading source facts never
    /// triggers an evaluation, so a source policy still needs no deployment inputs at all.
    /// </summary>
    private static JToken EvaluateExpression(EmitterContext context, SyntaxBase syntax, TestTargetFacts facts, string outputType, TestInputCase? inputs, TestEvaluatedFactsProvider? evaluated)
    {
        var target = TestTargetFactsSerializer.Serialize(facts);

        if (References(syntax, TestTargetType.EvaluatedPropertyName))
        {
            if (evaluated is null)
            {
                throw new InvalidOperationException("Evaluated values are not available for this target.");
            }

            target[TestTargetType.EvaluatedPropertyName] = TestTargetFactsSerializer.SerializeEvaluated(
                evaluated,
                includeOutputs: References(syntax, TestTargetType.OutputsPropertyName),
                includeWithModules: References(syntax, TestTargetType.WithModulesPropertyName),
                includeBodies: ReadsEvaluatedBody(context, syntax));
        }

        var seed = new JObject
        {
            [TestAssertion.TargetVariableName] = target,
        };

        return BicepValueEvaluator.Evaluate(context, syntax, outputType, seed, inputs?.Values, inputs?.Context);
    }

    /// <summary>
    /// Whether the expression reads the named branch of the target anywhere, including inside lambdas.
    /// </summary>
    private static bool References(SyntaxBase syntax, string propertyName) => SyntaxAggregator.Aggregate(
        syntax,
        seed: false,
        function: (found, node) => found || (node is PropertyAccessSyntax access && access.PropertyName.IdentifierName == propertyName),
        resultSelector: result => result,
        continuationFunction: (found, _) => !found);

    /// <summary>
    /// Whether the expression reads a body key of an evaluated instance. The type decides, not the key
    /// alone, so an output that happens to be called <c>location</c> does not count; a value whose type
    /// is not known is assumed to be one, since rendering a body that is not read costs nothing but
    /// omitting one that is would fail the read.
    /// </summary>
    private static bool ReadsEvaluatedBody(EmitterContext context, SyntaxBase syntax) => SyntaxAggregator.Aggregate(
        syntax,
        seed: false,
        function: (found, node) => found || node switch
        {
            PropertyAccessSyntax access => TestTargetType.EvaluatedBodyPropertyNames.Contains(access.PropertyName.IdentifierName) &&
                IsEvaluatedResourceOrUnknown(context.SemanticModel.GetTypeInfo(access.BaseExpression)),
            ArrayAccessSyntax { IndexExpression: StringSyntax index } access => index.TryGetLiteralValue() is { } key &&
                TestTargetType.EvaluatedBodyPropertyNames.Contains(key) &&
                IsEvaluatedResourceOrUnknown(context.SemanticModel.GetTypeInfo(access.BaseExpression)),
            _ => false,
        },
        resultSelector: result => result,
        continuationFunction: (found, _) => !found);

    private static bool IsEvaluatedResourceOrUnknown(TypeSymbol type) => type is AnyType || TestTargetType.IsEvaluatedResourceFact(type);

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
            ?? fact[TestTargetType.SymbolicNamePropertyName]?.Value<string>()
            ?? fact[TestTargetType.PathPropertyName]?.Value<string>();

        if (file is null || line is null)
        {
            return fact.ToString(Formatting.None);
        }

        return name is null ? $"{file}({line})" : $"{file}({line}): {name}";
    }

}
