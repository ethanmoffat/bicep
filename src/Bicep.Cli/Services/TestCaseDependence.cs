// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core;
using Bicep.Core.Semantics;
using Bicep.Core.Semantics.Namespaces;
using Bicep.Core.Syntax;
using Bicep.Core.TestFramework;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Cli.Services;

/// <summary>
/// Decides whether a test can give a different answer for different input cases. A case supplies the
/// test file's parameters and the ambient deployment context, and nothing else, so a test whose
/// assertions reach neither, and which reads no evaluated values, gives the same answer for every case.
/// Such a test is run once instead of once per case, so its single answer is not counted again for each.
/// </summary>
public static class TestCaseDependence
{
    /// <summary>
    /// Whether the test may read anything a case supplies. Anything the analysis cannot account for is
    /// assumed to depend on the case, so a test is only ever run fewer times when that is certain to be
    /// safe.
    /// </summary>
    public static bool DependsOnCase(SemanticModel testFileModel, TestDeclarationSyntax test)
    {
        // A test without assertions of its own runs the target's template, which takes the case's
        // parameters and deployment context.
        if (test.TryGetAssertionsSyntax() is not { } assertions || !assertions.Properties.Any())
        {
            return true;
        }

        var visitor = new Visitor(testFileModel);
        visitor.Visit(assertions);

        return visitor.DependsOnCase;
    }

    private sealed class Visitor(SemanticModel model) : AstVisitor
    {
        private readonly HashSet<Symbol> visited = [];

        public bool DependsOnCase { get; private set; }

        protected override void VisitInternal(SyntaxBase node)
        {
            if (!DependsOnCase)
            {
                base.VisitInternal(node);
            }
        }

        public override void VisitPropertyAccessSyntax(PropertyAccessSyntax syntax)
        {
            if (string.Equals(syntax.PropertyName.IdentifierName, TestTargetType.EvaluatedPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                DependsOnCase = true;
                return;
            }

            base.VisitPropertyAccessSyntax(syntax);
        }

        public override void VisitArrayAccessSyntax(ArrayAccessSyntax syntax)
        {
            // An index may read the evaluated values as well as a property access can, unless its key is
            // known from the source to name something else.
            if (model.GetTypeInfo(syntax.BaseExpression) is ObjectType indexed &&
                indexed.Properties.ContainsKey(TestTargetType.EvaluatedPropertyName) &&
                !(model.GetTypeInfo(syntax.IndexExpression) is StringLiteralType literal &&
                    !string.Equals(literal.RawStringValue, TestTargetType.EvaluatedPropertyName, StringComparison.OrdinalIgnoreCase)))
            {
                DependsOnCase = true;
                return;
            }

            base.VisitArrayAccessSyntax(syntax);
        }

        public override void VisitVariableAccessSyntax(VariableAccessSyntax syntax)
        {
            if (model.GetSymbolInfo(syntax) is TestTargetSymbol)
            {
                // The target is safe to read one named fact at a time, which the accesses above check.
                // Handed whole to a function or another expression, its evaluated values go with it.
                DependsOnCase |= model.Binder.GetParent(syntax) switch
                {
                    PropertyAccessSyntax access => !ReferenceEquals(access.BaseExpression, syntax),
                    ArrayAccessSyntax access => !ReferenceEquals(access.BaseExpression, syntax),
                    _ => true,
                };
                return;
            }

            Reach(model.GetSymbolInfo(syntax));
            base.VisitVariableAccessSyntax(syntax);
        }

        public override void VisitFunctionCallSyntax(FunctionCallSyntax syntax)
        {
            Reach(model.GetSymbolInfo(syntax));
            base.VisitFunctionCallSyntax(syntax);
        }

        public override void VisitInstanceFunctionCallSyntax(InstanceFunctionCallSyntax syntax)
        {
            Reach(model.GetSymbolInfo(syntax));
            base.VisitInstanceFunctionCallSyntax(syntax);
        }

        private void Reach(Symbol? symbol)
        {
            switch (symbol)
            {
                case LocalVariableSymbol or INamespaceSymbol:
                    return;
                case FunctionSymbol function:
                    // The az functions read the deployment context, which a case may set.
                    DependsOnCase |= function.DeclaringObject is not NamespaceType { ExtensionName: SystemNamespaceType.BuiltInName };
                    return;
                case VariableSymbol variable:
                    if (visited.Add(variable))
                    {
                        Visit(variable.DeclaringVariable.Value);
                    }
                    return;
                case DeclaredFunctionSymbol function:
                    if (visited.Add(function))
                    {
                        Visit(function.DeclaringFunction.Lambda);
                    }
                    return;
                default:
                    // A parameter is a case input. Anything else, such as an imported symbol or a name
                    // that did not bind, cannot be accounted for here.
                    DependsOnCase = true;
                    return;
            }
        }
    }
}
