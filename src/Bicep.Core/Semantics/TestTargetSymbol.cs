// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.Semantics;

/// <summary>
/// The compiler-provided 'target' symbol, describing the file a test assertion is currently running
/// against. It is deliberately not a language keyword: it exists only inside the 'assertions' object
/// of a test declaration, and is invisible everywhere else.
/// </summary>
public class TestTargetSymbol : DeclaredSymbol
{
    private class TestTargetNameSource : ISymbolNameSource
    {
        private readonly TestDeclarationSyntax test;

        public TestTargetNameSource(TestDeclarationSyntax test)
        {
            this.test = test;
        }

        public bool IsValid => true;

        public TextSpan Span => test.Keyword.Span;
    }

    public TestTargetSymbol(ISymbolContext context, TestDeclarationSyntax declaringTest, SyntaxBase declaringSyntax, TypeSymbol declaredType)
        : base(context, LanguageConstants.TestTargetName, declaringSyntax, new TestTargetNameSource(declaringTest))
    {
        this.DeclaringTest = declaringTest;
        this.DeclaredType = declaredType;
    }

    public TestDeclarationSyntax DeclaringTest { get; }

    public TypeSymbol DeclaredType { get; }

    /// <summary>
    /// The type is supplied by the compiler rather than inferred from the declaring syntax, which is
    /// the assertions object this symbol is scoped to.
    /// </summary>
    public new TypeSymbol Type => DeclaredType;

    public override void Accept(SymbolVisitor visitor) => visitor.VisitTestTargetSymbol(this);

    public override SymbolKind Kind => SymbolKind.Local;
}
