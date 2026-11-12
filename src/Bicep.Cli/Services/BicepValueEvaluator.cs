// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Bicep.Core.Emit;
using Bicep.Core.Intermediate;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;
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

    private const string LanguageVersion = "2.0";

    private static readonly ConditionalWeakTable<SemanticModel, JToken> EmittedFiles = new();

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

    /// <summary>
    /// The missing input behind a failure, however deeply the evaluator wrapped it, or null when the
    /// failure had another cause.
    /// </summary>
    public static MissingInputException? FindMissingInput(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MissingInputException missing)
            {
                return missing;
            }
        }

        return null;
    }

    public static JToken Evaluate(
        EmitterContext context,
        SyntaxBase syntax,
        string outputType,
        JObject? seedVariables = null,
        IReadOnlyDictionary<string, JToken>? inputValues = null,
        TestDeploymentContext? deploymentContext = null)
    {
        var expression = new ExpressionBuilder(context).Convert(syntax);
        var reachable = CollectReachable(context, expression);

        var variables = seedVariables is null ? new JObject() : (JObject)Escape(seedVariables);

        // A load function's result is computed at compile time and staged in a synthesized variable,
        // which every expression that calls it reads.
        foreach (var functionVariable in reachable.FunctionVariables)
        {
            variables[functionVariable.Name] = Emit(context, functionVariable.Value);
        }

        var copies = new JArray();
        var declarations = reachable.Variables.Select(variable => (variable.Name, Value: new ExpressionBuilder(context).Convert(variable.DeclaringVariable.Value)));

        if (reachable.UsesImports)
        {
            // An imported variable is declared under the name the import closure gives it, and may read
            // any other variable in the closure, so the whole closure is declared.
            declarations = declarations.Concat(context.SemanticModel.ImportClosureInfo.ImportedVariablesInClosure.Select(variable => (variable.Name, variable.Value)));
        }

        foreach (var (name, value) in declarations)
        {
            if (value is ForLoopExpression forLoop)
            {
                // A variable whose value is a for-expression is declared as a copy loop, which is how a
                // deployed template declares it.
                copies.Add(Emit(context, emitter => emitter.EmitCopyObject(name, forLoop.Expression, forLoop.Body)));
            }
            else
            {
                variables[name] = Emit(context, value);
            }
        }

        if (copies.Count > 0)
        {
            variables["copy"] = copies;
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

        if (BuildParameters(context, reachable.Parameters, inputValues, deploymentContext) is { Count: > 0 } parameters)
        {
            template["parameters"] = parameters;
        }

        if (reachable.Functions.Length > 0 || reachable.UsesImports)
        {
            DeclareFunctions(context.SemanticModel, template);
        }

        // The engine honors a nullable parameter only in a template that declares a language version,
        // as a template Bicep emits for one does.
        if (template["languageVersion"] is null &&
            template["parameters"] is JObject declared &&
            declared.Properties().Any(p => p.Value["nullable"] is not null))
        {
            DeclareLanguageVersion(template, LanguageVersion);
        }

        var evaluated = TemplateEvaluator.Evaluate(template, configBuilder: (deploymentContext ?? TestDeploymentContext.Empty).Apply).ToJToken();

        return evaluated["outputs"]?["result"]?["value"] ?? JValue.CreateNull();
    }

    /// <summary>
    /// Declares the user-defined functions the file declares or imports.
    ///
    /// They are copied from the file's own emitted template, so each one is declared exactly as a
    /// deployed template would declare it, along with the type definitions its parameters and result
    /// refer to. A function cannot read anything outside its own arguments, so declaring every one is
    /// safe. The file is emitted once, and only when a function or an import is used at all.
    /// </summary>
    private static void DeclareFunctions(SemanticModel model, JObject template)
    {
        var emitted = EmittedFiles.GetValue(model, m => TestTemplateEmitter.Emit(m));

        if (emitted["functions"] is JArray functions)
        {
            template["functions"] = functions.DeepClone();
        }

        if (emitted["definitions"] is JObject definitions)
        {
            template["definitions"] = definitions.DeepClone();
        }

        if (emitted["languageVersion"] is { } languageVersion)
        {
            // A template that refers to type definitions declares its language version.
            DeclareLanguageVersion(template, languageVersion.DeepClone());
        }
    }

    /// <summary>
    /// Declares the template's language version. At any version, its resources are keyed by symbolic name.
    /// </summary>
    private static void DeclareLanguageVersion(JObject template, JToken languageVersion)
    {
        template["languageVersion"] = languageVersion;
        template["resources"] = new JObject();
    }

    /// <summary>
    /// Copies seed data into the synthetic template as literal data.
    ///
    /// Seed values are facts, not expressions. A value that happens to read like a template expression -
    /// an output the target could not compute offline, for instance - would otherwise be evaluated a
    /// second time here and fail for reasons that have nothing to do with the assertion, so it is
    /// escaped and reaches the assertion as the text it is.
    /// </summary>
    private static JToken Escape(JToken token)
    {
        switch (token)
        {
            case JObject source:
                var mapped = new JObject();

                foreach (var property in source.Properties())
                {
                    mapped[property.Name] = Escape(property.Value);
                }

                return mapped;

            case JArray array:
                return new JArray(array.Select(Escape));

            case JValue { Type: JTokenType.String } value when value.Value<string>() is { Length: > 1 } text && text[0] == '[' && text[^1] == ']':
                return new JValue($"[{text}");

            default:
                return token.DeepClone();
        }
    }

    /// <summary>
    /// Declares each reached input with the value the case supplied, falling back to the declared
    /// default. The declared type is derived from the resolved value, since evaluation only needs the
    /// value itself; the compiler has already checked the value against the author's declared type.
    /// </summary>
    private static JObject BuildParameters(
        EmitterContext context,
        IEnumerable<ParameterSymbol> parameters,
        IReadOnlyDictionary<string, JToken>? inputValues,
        TestDeploymentContext? deploymentContext)
    {
        var result = new JObject();

        foreach (var parameter in parameters)
        {
            var value = ResolveInput(context, parameter, inputValues, deploymentContext);

            // A nullable input with no value, or with an explicit null, is declared nullable with no
            // default, which is how the engine yields null for an input nothing supplied. A null
            // default is not the same thing: the engine treats it as a missing value.
            result[parameter.Name] = value is null or { Type: JTokenType.Null }
                ? new JObject
                {
                    ["type"] = ArmTypeNameOf(TypeHelper.TryRemoveNullability(parameter.Type) ?? parameter.Type),
                    ["nullable"] = true,
                }
                : new JObject
                {
                    ["type"] = ArmTypeNameOf(value),
                    ["defaultValue"] = value,
                };
        }

        return result;
    }

    /// <summary>
    /// Resolves the value an input takes: the case's value, then the declared default. A nullable
    /// input with neither resolves to no value at all, which evaluates to null.
    /// </summary>
    private static JToken? ResolveInput(
        EmitterContext context,
        ParameterSymbol parameter,
        IReadOnlyDictionary<string, JToken>? inputValues,
        TestDeploymentContext? deploymentContext)
    {
        if (inputValues is not null && inputValues.TryGetValue(parameter.Name, out var supplied))
        {
            return supplied;
        }

        if (parameter.DeclaringParameter.Modifier is ParameterDefaultValueSyntax defaultValue)
        {
            return Evaluate(context, defaultValue.DefaultValue, "string", deploymentContext: deploymentContext);
        }

        if (TypeHelper.IsNullable(parameter.Type))
        {
            return null;
        }

        throw new MissingInputException(parameter.Name);
    }

    private static string ArmTypeNameOf(TypeSymbol type) => type switch
    {
        StringLiteralType or StringType => "string",
        IntegerLiteralType or IntegerType => "int",
        BooleanLiteralType or BooleanType => "bool",
        ArrayType => "array",
        _ => "object",
    };

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
        var functionVariables = new List<FunctionVariable>();
        var functions = new List<DeclaredFunctionSymbol>();
        var seenVariables = new HashSet<VariableSymbol>();
        var seenParameters = new HashSet<ParameterSymbol>();
        var seenFunctionVariables = new HashSet<string>();
        var seenFunctions = new HashSet<DeclaredFunctionSymbol>();
        var usesImports = false;
        var functionVariablesByName = context.FunctionVariables.Values.ToDictionary(x => x.Name);
        var queue = new Queue<Expression>();

        queue.Enqueue(expression);

        while (queue.Count > 0)
        {
            var collector = new SymbolReferenceCollector();
            collector.Visit(queue.Dequeue());
            usesImports |= collector.UsesImports;

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

            foreach (var name in collector.SynthesizedVariables)
            {
                if (functionVariablesByName.TryGetValue(name, out var functionVariable) && seenFunctionVariables.Add(name))
                {
                    functionVariables.Add(functionVariable);
                }
            }

            foreach (var function in collector.Functions)
            {
                if (seenFunctions.Add(function))
                {
                    functions.Add(function);
                    queue.Enqueue(new ExpressionBuilder(context).Convert(function.DeclaringFunction));
                }
            }
        }

        return new([.. variables], [.. parameters], [.. functionVariables], [.. functions], usesImports);
    }

    private static JToken Emit(EmitterContext context, Expression expression)
        => Emit(context, emitter => emitter.EmitExpression(expression));

    private static JToken Emit(EmitterContext context, Action<ExpressionEmitter> emit)
    {
        var textWriter = new StringWriter();
        using var writer = new PositionTrackingJsonTextWriter(textWriter)
        {
            CloseOutput = false,
            Formatting = Formatting.None,
        };

        emit(new ExpressionEmitter(writer, context));
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
        if (FindMissingInput(exception) is { } missing)
        {
            return missing.Message;
        }

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

        // The engine only knows a metadata key is absent. The author needs to know which piece of
        // simulated context they never supplied.
        return Translate(message.TrimEnd());
    }

    /// <summary>
    /// Rewrites engine phrases that describe an internal metadata lookup rather than the authoring
    /// mistake that caused it. Whatever surrounds the phrase, such as the resource that read the
    /// value, is kept.
    /// </summary>
    public static string Translate(string message)
        => message
            .Replace(MissingMetadata("DEPLOYMENT"), "deployment() was evaluated but no deployment name was supplied. Set 'deploymentName' in the input file's deploymentContext, or override it for this case with @deploymentName().", StringComparison.OrdinalIgnoreCase)
            .Replace(MissingMetadata("ENVIRONMENT"), "environment() was evaluated but no environment was supplied. Set 'environment' in the input file's deploymentContext, or override it for this case with @environment().", StringComparison.OrdinalIgnoreCase);

    private static string MissingMetadata(string name) => $"The deployment metadata '{name}' is not valid.";

    private record ReachableSymbols(ImmutableArray<VariableSymbol> Variables, ImmutableArray<ParameterSymbol> Parameters, ImmutableArray<FunctionVariable> FunctionVariables, ImmutableArray<DeclaredFunctionSymbol> Functions, bool UsesImports);

    private class SymbolReferenceCollector : ExpressionVisitor
    {
        public List<VariableSymbol> Variables { get; } = [];

        public List<ParameterSymbol> Parameters { get; } = [];

        public List<string> SynthesizedVariables { get; } = [];

        public List<DeclaredFunctionSymbol> Functions { get; } = [];

        public bool UsesImports { get; private set; }

        public override void VisitImportedVariableReferenceExpression(ImportedVariableReferenceExpression expression)
        {
            UsesImports = true;

            base.VisitImportedVariableReferenceExpression(expression);
        }

        public override void VisitWildcardImportVariablePropertyReferenceExpression(WildcardImportVariablePropertyReferenceExpression expression)
        {
            UsesImports = true;

            base.VisitWildcardImportVariablePropertyReferenceExpression(expression);
        }

        public override void VisitImportedUserDefinedFunctionCallExpression(ImportedUserDefinedFunctionCallExpression expression)
        {
            UsesImports = true;

            base.VisitImportedUserDefinedFunctionCallExpression(expression);
        }

        public override void VisitWildcardImportInstanceFunctionCallExpression(WildcardImportInstanceFunctionCallExpression expression)
        {
            UsesImports = true;

            base.VisitWildcardImportInstanceFunctionCallExpression(expression);
        }

        public override void VisitUserDefinedFunctionCallExpression(UserDefinedFunctionCallExpression expression)
        {
            Functions.Add(expression.Symbol);

            base.VisitUserDefinedFunctionCallExpression(expression);
        }

        public override void VisitSynthesizedVariableReferenceExpression(SynthesizedVariableReferenceExpression expression)
        {
            SynthesizedVariables.Add(expression.Name);

            base.VisitSynthesizedVariableReferenceExpression(expression);
        }

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
