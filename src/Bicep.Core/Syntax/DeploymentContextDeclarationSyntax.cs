// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Text;

namespace Bicep.Core.Syntax
{
    /// <summary>
    /// The file-level deployment context of a test parameters file. This is runner-owned evaluation
    /// metadata rather than data the test can read: it describes the simulated deployment an input
    /// case is computed against, and it can never change which targets a test applies to.
    /// </summary>
    public class DeploymentContextDeclarationSyntax : StatementSyntax, ITopLevelDeclarationSyntax
    {
        public DeploymentContextDeclarationSyntax(IEnumerable<SyntaxBase> leadingNodes, Token keyword, SyntaxBase assignment, SyntaxBase value)
            : base(leadingNodes)
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.DeploymentContextKeyword);

            this.Keyword = keyword;
            this.Assignment = assignment;
            this.Value = value;
        }

        public Token Keyword { get; }

        public SyntaxBase Assignment { get; }

        public SyntaxBase Value { get; }

        public ObjectSyntax? Body => this.Value as ObjectSyntax;

        public override void Accept(ISyntaxVisitor visitor)
            => visitor.VisitDeploymentContextDeclarationSyntax(this);

        public override TextSpan Span => TextSpan.Between(this.Keyword, this.Value);
    }
}
