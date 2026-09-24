// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.CodeAction;
using Bicep.Core.Diagnostics;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Syntax.Visitors;
using Bicep.Core.Text;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.Analyzers.Linter.Rules;

public sealed class NoImpossibleComparisonsRule : LinterRuleBase
{
    public new const string Code = "no-impossible-comparisons";

    public NoImpossibleComparisonsRule() : base(
        code: Code,
        description: CoreResources.NoImpossibleComparisonsRuleDescription,
        LinterRuleCategory.PotentialCodeIssues)
    { }

    public override string FormatMessage(params object[] values)
        => (string)values[0];

    public override IEnumerable<IDiagnostic> AnalyzeInternal(SemanticModel model, DiagnosticLevel diagnosticLevel)
    {
        foreach (var comparison in SyntaxAggregator.AggregateByType<BinaryOperationSyntax>(model.Root.Syntax))
        {
            if (comparison.Operator is not (BinaryOperator.Equals or BinaryOperator.NotEquals or BinaryOperator.EqualsInsensitive or BinaryOperator.NotEqualsInsensitive))
            {
                continue;
            }

            var leftType = model.GetTypeInfo(comparison.LeftExpression);
            var rightType = model.GetTypeInfo(comparison.RightExpression);
            if (GetPossibleValues(leftType) is not { } leftValues ||
                GetPossibleValues(rightType) is not { } rightValues)
            {
                continue;
            }

            // A constant compared with a constant is left alone: it is how a template hard-codes a
            // switch, and folding it is the compiler's business. What this rule is for is a value
            // compared with something outside the set of alternatives its type declares.
            if (leftValues.Length < 2 && rightValues.Length < 2)
            {
                continue;
            }

            var ignoreCase = comparison.Operator is BinaryOperator.EqualsInsensitive or BinaryOperator.NotEqualsInsensitive;
            if (leftValues.Any(left => rightValues.Any(right => CanEqual(left, right, ignoreCase))))
            {
                continue;
            }

            var alwaysTrue = comparison.Operator is BinaryOperator.NotEquals or BinaryOperator.NotEqualsInsensitive;
            var message = string.Format(
                CoreResources.NoImpossibleComparisonsRuleMessageFormat,
                alwaysTrue ? LanguageConstants.TrueKeyword : LanguageConstants.FalseKeyword,
                leftType.Name,
                rightType.Name);

            if ((TryGetSuggestion(comparison.LeftExpression, leftValues, rightValues) ?? TryGetSuggestion(comparison.RightExpression, rightValues, leftValues)) is not { } suggestion)
            {
                yield return CreateDiagnosticForSpan(diagnosticLevel, comparison.Span, message);
                continue;
            }

            var replacement = SyntaxFactory.CreateStringLiteral(suggestion.Value).ToString();
            var fix = new CodeFix(
                string.Format(CoreResources.NoImpossibleComparisonsRuleCodeFix, replacement),
                false,
                CodeFixKind.QuickFix,
                new CodeReplacement(suggestion.Literal.Span, replacement));
            string messageWithSuggestion = $"{message} {string.Format(CoreResources.NoImpossibleComparisonsRuleSuggestionFormat, replacement)}";
            yield return CreateFixableDiagnosticForSpan(diagnosticLevel, comparison.Span, fix, messageWithSuggestion);
        }
    }

    /// <summary>
    /// The values a type allows, when it allows only a finite set of literals. Types marked as loose
    /// are excluded: resource type definitions carry enumerations that are known to be incomplete,
    /// and a value outside one of those is not evidence of a mistake.
    /// </summary>
    private static ImmutableArray<TypeSymbol>? GetPossibleValues(TypeSymbol type)
    {
        ImmutableArray<TypeSymbol> members = type is UnionType union ? [.. union.Members.Select(member => member.Type)] : [type];
        if (members.IsEmpty || IsLoose(type))
        {
            return null;
        }

        foreach (var member in members)
        {
            if (member is not (StringLiteralType or IntegerLiteralType or BooleanLiteralType or NullType) || IsLoose(member))
            {
                return null;
            }
        }

        return members;
    }

    private static bool IsLoose(TypeSymbol type)
        => type.ValidationFlags.HasFlag(TypeSymbolValidationFlags.WarnOnTypeMismatch) ||
            type.ValidationFlags.HasFlag(TypeSymbolValidationFlags.AllowLooseAssignment);

    private static bool CanEqual(TypeSymbol left, TypeSymbol right, bool ignoreCase) => (left, right) switch
    {
        (StringLiteralType l, StringLiteralType r) => string.Equals(l.RawStringValue, r.RawStringValue, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
        (IntegerLiteralType l, IntegerLiteralType r) => l.Value == r.Value,
        (BooleanLiteralType l, BooleanLiteralType r) => l.Value == r.Value,
        (NullType, NullType) => true,
        _ => false,
    };

    private static (StringSyntax Literal, string Value)? TryGetSuggestion(SyntaxBase expression, ImmutableArray<TypeSymbol> values, ImmutableArray<TypeSymbol> alternatives)
    {
        if (expression is not StringSyntax literal ||
            literal.TryGetLiteralValue() is not { } value ||
            values.Length != 1)
        {
            return null;
        }

        var candidates = alternatives.OfType<StringLiteralType>().Select(alternative => alternative.RawStringValue);
        return SpellChecker.GetSpellingSuggestion(value, candidates) is { } suggestion ? (literal, suggestion) : null;
    }
}
