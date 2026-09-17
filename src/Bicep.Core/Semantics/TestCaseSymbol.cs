// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Syntax;

namespace Bicep.Core.Semantics
{
    /// <summary>
    /// A named input case declared in a test parameters file.
    /// </summary>
    public class TestCaseSymbol : DeclaredSymbol
    {
        public TestCaseSymbol(ISymbolContext context, string name, TestCaseDeclarationSyntax declaringSyntax)
            : base(context, name, declaringSyntax, declaringSyntax.Name)
        {
        }

        public TestCaseDeclarationSyntax DeclaringTestCase => (TestCaseDeclarationSyntax)this.DeclaringSyntax;

        public override SymbolKind Kind => SymbolKind.TestCase;

        public override IEnumerable<Symbol> Descendants
        {
            get
            {
                yield return this.Type;
            }
        }

        public override void Accept(SymbolVisitor visitor) => visitor.VisitTestCaseSymbol(this);
    }
}
