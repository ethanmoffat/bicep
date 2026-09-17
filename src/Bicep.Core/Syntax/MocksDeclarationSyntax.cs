// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Text;

namespace Bicep.Core.Syntax
{
    /// <summary>
    /// The file-level runtime mocks of a test file. These supply synthetic responses for the external
    /// reads a template cannot compute from its own source, parameters or deployment context. They are
    /// owned by the test rather than by an input file, so the synthetic values stay reviewed next to
    /// the assertions that depend on them.
    /// </summary>
    public class MocksDeclarationSyntax : StatementSyntax, ITopLevelDeclarationSyntax
    {
        public MocksDeclarationSyntax(IEnumerable<SyntaxBase> leadingNodes, Token keyword, SyntaxBase assignment, SyntaxBase value)
            : base(leadingNodes)
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.MocksKeyword);

            this.Keyword = keyword;
            this.Assignment = assignment;
            this.Value = value;
        }

        public Token Keyword { get; }

        public SyntaxBase Assignment { get; }

        public SyntaxBase Value { get; }

        public ObjectSyntax? Body => this.Value as ObjectSyntax;

        public override void Accept(ISyntaxVisitor visitor)
            => visitor.VisitMocksDeclarationSyntax(this);

        public override TextSpan Span => TextSpan.Between(this.Keyword, this.Value);
    }
}
