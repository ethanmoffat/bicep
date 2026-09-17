// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Emit;
using Bicep.Core.Intermediate;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Evaluates an ordinary Bicep expression offline, with no deployment and no provider calls.
///
/// The expression is converted to its template-language form and evaluated inside a synthetic
/// template. Only the variables and parameters the expression actually reaches are materialized, so an
/// unrelated declaration elsewhere in the file can never break an evaluation that does not use it.
/// </summary>
public static class BicepValueEvaluator
{
    private const string DeploymentTemplateSchema = "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#";

    /// <summary>
    /// The name of the variable an expression is staged in. Outputs are evaluated optimistically,
    /// which leaves a failed expression sitting there as unevaluated text; variable evaluation reports
    /// the failure instead, which is what lets a broken expression say why it could not be evaluated.
    /// </summary>
    public const string ResultVariableName = "$assertionResult";

    /// <summary>
    /// Raised when an expression reaches an input that no case supplied and that declares no default.
    /// </summary>
    public class MissingInputException(string parameterName)
        : Exception($"The input \"{parameterName}\" has no value. Supply it from a test case or give it a default.")
    {
        public string ParameterName { get; } = parameterName;
    }

    public static JToken Evaluate(
        EmitterContext context,
        SyntaxBase syntax,
        string outputType,
        JObject? seedVariables = null,
        IReadOnlyDictionary<string, JToken>? inputValues = null)
    {
        var expression = new ExpressionBuilder(context).Convert(syntax);
        var reachable = CollectReachable(context, expression);

        var variables = seedVariables is null ? new JObject() : (JObject)seedVariables.DeepClone();

        foreach (var variable in reachable.Variables)
        {
            variables[variable.Name] = Emit(context, new ExpressionBuilder(context).Convert(variable.DeclaringVariable.Value));
        }

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

        if (BuildParameters(context, reachable.Parameters, inputValues) is { Count: > 0 } parameters)
        {
            template["parameters"] = parameters;
        }

        var evaluated = TemplateEvaluator.Evaluate(template).ToJToken();

        return evaluated["outputs"]?["result"]?["value"] ?? JValue.CreateNull();
    }

    /// <summary>
    /// Declares each reached input with the value the case supplied, falling back to the declared
    /// default. The declared type is derived from the resolved value, since evaluation only needs the
    /// value itself; the compiler has already checked the value against the author's declared type.
    /// </summary>
    private static JObject BuildParameters(
        EmitterContext context,
        IEnumerable<ParameterSymbol> parameters,
        IReadOnlyDictionary<string, JToken>? inputValues)
    {
        var result = new JObject();

        foreach (var parameter in parameters)
        {
            var value = ResolveInput(context, parameter, inputValues);

            result[parameter.Name] = new JObject
            {
                ["type"] = ArmTypeNameOf(value),
                ["defaultValue"] = value,
            };
        }

        return result;
    }

    private static JToken ResolveInput(
        EmitterContext context,
        ParameterSymbol parameter,
        IReadOnlyDictionary<string, JToken>? inputValues)
    {
        if (inputValues is not null && inputValues.TryGetValue(parameter.Name, out var supplied))
        {
            return supplied;
        }

        if (parameter.DeclaringParameter.Modifier is ParameterDefaultValueSyntax defaultValue)
        {
            return Evaluate(context, defaultValue.DefaultValue, "string");
        }

        throw new MissingInputException(parameter.Name);
    }

    private static string ArmTypeNameOf(JToken value) => value.Type switch
    {
        JTokenType.Integer or JTokenType.Float => "int",
        JTokenType.Boolean => "bool",
        JTokenType.Array => "array",
        JTokenType.Object => "object",
        _ => "string",
    };

    private static ReachableSymbols CollectReachable(EmitterContext context, Expression expression)
    {
        var variables = new List<VariableSymbol>();
        var parameters = new List<ParameterSymbol>();
        var seenVariables = new HashSet<VariableSymbol>();
        var seenParameters = new HashSet<ParameterSymbol>();
        var queue = new Queue<Expression>();

        queue.Enqueue(expression);

        while (queue.Count > 0)
        {
            var collector = new SymbolReferenceCollector();
            collector.Visit(queue.Dequeue());

            foreach (var variable in collector.Variables)
            {
                if (seenVariables.Add(variable))
                {
                    variables.Add(variable);
                    queue.Enqueue(new ExpressionBuilder(context).Convert(variable.DeclaringVariable.Value));
                }
            }

            foreach (var parameter in collector.Parameters)
            {
                if (seenParameters.Add(parameter))
                {
                    parameters.Add(parameter);
                }
            }
        }

        return new([.. variables], [.. parameters]);
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
    /// Reduces an evaluation failure to the part an author can act on. The evaluator's own message
    /// embeds the whole synthetic template and the internal variable it was staged in, neither of
    /// which corresponds to anything the author wrote.
    /// </summary>
    public static string Sanitize(Exception exception)
    {
        if (exception is MissingInputException)
        {
            return exception.Message;
        }

        var innermost = exception;

        while (innermost.InnerException is { } inner)
        {
            if (inner is MissingInputException)
            {
                return inner.Message;
            }

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

    private record ReachableSymbols(ImmutableArray<VariableSymbol> Variables, ImmutableArray<ParameterSymbol> Parameters);

    private class SymbolReferenceCollector : ExpressionVisitor
    {
        public List<VariableSymbol> Variables { get; } = [];

        public List<ParameterSymbol> Parameters { get; } = [];

        public override void VisitVariableReferenceExpression(VariableReferenceExpression expression)
        {
            Variables.Add(expression.Variable);

            base.VisitVariableReferenceExpression(expression);
        }

        public override void VisitParametersReferenceExpression(ParametersReferenceExpression expression)
        {
            Parameters.Add(expression.Parameter);

            base.VisitParametersReferenceExpression(expression);
        }
    }
}
