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
    private const string DeploymentTemplateSchema = "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#";

    private const string ResultVariableName = "$assertionResult";

    /// <summary>
    /// Evaluates every assertion the test declares. An assertion that cannot be evaluated fails with its
    /// own error rather than aborting the ones beside it, so a single broken query never hides the
    /// outcome of the rest of the policy.
    /// </summary>
    public static ImmutableArray<AssertionResult> Evaluate(
        SemanticModel testFileModel,
        TestDeclarationSyntax testDeclaration,
        TestTargetFacts facts)
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

            results.Add(EvaluateOne(context, name, assertion.Value as ObjectSyntax, facts));
        }

        return results.ToImmutable();
    }

    private static AssertionResult EvaluateOne(EmitterContext context, string name, ObjectSyntax? body, TestTargetFacts facts)
    {
        var message = (body?.TryGetPropertyByName(TestAssertion.MessagePropertyName)?.Value as StringSyntax)?.TryGetLiteralValue();
        var passWhen = body?.TryGetPropertyByName(TestAssertion.PassWhenPropertyName)?.Value;
        var failOn = body?.TryGetPropertyByName(TestAssertion.FailOnPropertyName)?.Value;

        try
        {
            if (passWhen is not null)
            {
                var value = EvaluateExpression(context, passWhen, facts, "bool");

                return new AssertionResult(name, value.Type == JTokenType.Boolean && value.Value<bool>())
                {
                    Message = message,
                };
            }

            if (failOn is not null)
            {
                var value = EvaluateExpression(context, failOn, facts, "array");
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
            return new AssertionResult(name, false) { Message = message, Error = Sanitize(exception) };
        }
    }

    /// <summary>
    /// Converts a Bicep expression to its template-language form and evaluates it with the target facts
    /// in scope, along with any variables the test file contributes to the expression.
    /// </summary>
    private static JToken EvaluateExpression(EmitterContext context, SyntaxBase syntax, TestTargetFacts facts, string outputType)
    {
        var expression = new ExpressionBuilder(context).Convert(syntax);
        var variables = BuildVariables(context, expression, facts);

        // The assertion is evaluated as a variable rather than directly as an output. Outputs are
        // evaluated optimistically, which leaves a failed expression sitting there as unevaluated
        // text; variable evaluation reports the failure, which is what lets a broken assertion say
        // why it could not be judged instead of quietly failing.
        variables[ResultVariableName] = Emit(context, expression);

        var template = new JObject
        {
            ["$schema"] = DeploymentTemplateSchema,
            ["contentVersion"] = "1.0.0.0",
            ["variables"] = variables,
            ["resources"] = new JArray(),
            ["outputs"] = new JObject
            {
                ["result"] = new JObject
                {
                    ["type"] = outputType,
                    ["value"] = $"[variables('{ResultVariableName}')]",
                },
            },
        };

        var evaluated = TemplateEvaluator.Evaluate(template).ToJToken();

        return evaluated["outputs"]?["result"]?["value"] ?? JValue.CreateNull();
    }

    private static JObject BuildVariables(EmitterContext context, Expression expression, TestTargetFacts facts)
    {
        var variables = new JObject
        {
            [TestAssertion.TargetVariableName] = TestTargetFactsSerializer.Serialize(facts),
        };

        // Only the variables the assertion actually reaches are materialized. A test file is free to
        // declare variables that an individual assertion does not use, and those must not be able to
        // break an unrelated assertion.
        foreach (var variable in CollectReachableVariables(context, expression))
        {
            variables[variable.Name] = Emit(context, new ExpressionBuilder(context).Convert(variable.DeclaringVariable.Value));
        }

        return variables;
    }

    private static IEnumerable<VariableSymbol> CollectReachableVariables(EmitterContext context, Expression expression)
    {
        var collected = new List<VariableSymbol>();
        var seen = new HashSet<VariableSymbol>();
        var queue = new Queue<Expression>();

        queue.Enqueue(expression);

        while (queue.Count > 0)
        {
            var collector = new VariableReferenceCollector();
            collector.Visit(queue.Dequeue());

            foreach (var variable in collector.Variables)
            {
                if (seen.Add(variable))
                {
                    collected.Add(variable);
                    queue.Enqueue(new ExpressionBuilder(context).Convert(variable.DeclaringVariable.Value));
                }
            }
        }

        return collected;
    }

    private static JToken Emit(EmitterContext context, Expression expression)
    {
        var textWriter = new StringWriter();
        using var writer = new PositionTrackingJsonTextWriter(textWriter)
        {
            CloseOutput = false,
            Formatting = Formatting.None,
        };

        new ExpressionEmitter(writer, context).EmitExpression(expression);
        writer.Flush();

        return JToken.Parse(textWriter.ToString());
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

    /// <summary>
    /// Reduces an evaluation failure to the part an author can act on. The evaluator's own message
    /// embeds the whole synthetic template and the internal variable it was staged in, neither of
    /// which corresponds to anything the author wrote.
    /// </summary>
    private static string Sanitize(Exception exception)
    {
        var innermost = exception;

        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        var message = innermost.Message;
        var lineBreak = message.IndexOfAny(['\r', '\n']);

        if (lineBreak >= 0)
        {
            message = message[..lineBreak];
        }

        var staging = $"The template variable '{ResultVariableName}' is not valid: ";

        if (message.StartsWith(staging, StringComparison.Ordinal))
        {
            message = message[staging.Length..];
        }

        return message.TrimEnd();
    }

    private class VariableReferenceCollector : ExpressionVisitor
    {
        public List<VariableSymbol> Variables { get; } = [];

        public override void VisitVariableReferenceExpression(VariableReferenceExpression expression)
        {
            Variables.Add(expression.Variable);

            base.VisitVariableReferenceExpression(expression);
        }
    }
}
