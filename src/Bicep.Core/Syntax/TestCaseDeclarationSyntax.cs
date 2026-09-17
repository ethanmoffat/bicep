// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Text;

namespace Bicep.Core.Syntax
{
    /// <summary>
    /// One named input case in a test parameters file. A case supplies values for the inputs its
    /// bound test file declares; it never selects or filters the targets the test applies to.
    /// </summary>
    public class TestCaseDeclarationSyntax : StatementSyntax, ITopLevelNamedDeclarationSyntax
    {
        public TestCaseDeclarationSyntax(IEnumerable<SyntaxBase> leadingNodes, Token keyword, IdentifierSyntax name, SyntaxBase assignment, SyntaxBase value)
            : base(leadingNodes)
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.TestCaseKeyword);
            AssertSyntaxType(name, nameof(name), typeof(IdentifierSyntax));

            this.Keyword = keyword;
            this.Name = name;
            this.Assignment = assignment;
            this.Value = value;
        }

        public Token Keyword { get; }

        public IdentifierSyntax Name { get; }

        public SyntaxBase Assignment { get; }

        public SyntaxBase Value { get; }

        public ObjectSyntax? Body => this.Value as ObjectSyntax;

        public override void Accept(ISyntaxVisitor visitor)
            => visitor.VisitTestCaseDeclarationSyntax(this);

        public override TextSpan Span => TextSpan.Between(this.Keyword, this.Value);
    }
}
