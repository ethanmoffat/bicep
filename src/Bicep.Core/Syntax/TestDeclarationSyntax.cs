// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Registry;
using Bicep.Core.Text;

namespace Bicep.Core.Syntax
{
    public class TestDeclarationSyntax : StatementSyntax, ITopLevelNamedDeclarationSyntax, IArtifactReferenceSyntax
    {
        public TestDeclarationSyntax(IEnumerable<SyntaxBase> leadingNodes, Token keyword, IdentifierSyntax name, SyntaxBase? path, SyntaxBase assignment, SyntaxBase value)
            : base(leadingNodes)
        {
            AssertKeyword(keyword, nameof(keyword), LanguageConstants.TestKeyword);
            AssertSyntaxType(path, nameof(path), typeof(StringSyntax), typeof(SkippedTriviaSyntax));
            AssertTokenType(keyword, nameof(keyword), TokenType.Identifier);
            AssertSyntaxType(assignment, nameof(assignment), typeof(Token), typeof(SkippedTriviaSyntax));
            AssertTokenType(assignment as Token, nameof(assignment), TokenType.Assignment);
            AssertSyntaxType(value, nameof(value), typeof(SkippedTriviaSyntax), typeof(ObjectSyntax));

            this.Keyword = keyword;
            this.Name = name;
            this.Path = path;
            this.Assignment = assignment;
            this.Value = value;
        }

        public Token Keyword { get; }

        public IdentifierSyntax Name { get; }

        /// <summary>
        /// The literal target path, or null when the test selects its targets through a body-owned
        /// <see cref="LanguageConstants.TestMatchPropertyName"/> selector instead.
        /// </summary>
        public SyntaxBase? Path { get; }

        /// <summary>
        /// Whether this test declares no literal target path. Such a test selects its targets through
        /// a body-owned selector, so it resolves to zero or more targets rather than exactly one.
        /// </summary>
        public bool IsTargetless => this.Path is null;

        public SyntaxBase Assignment { get; }

        public SyntaxBase Value { get; }

        public override void Accept(ISyntaxVisitor visitor) => visitor.VisitTestDeclarationSyntax(this);

        public override TextSpan Span => TextSpan.Between(this.LeadingNodes.FirstOrDefault() ?? this.Keyword, this.Value);

        // Diagnostics about the target are reported on the path when there is one, and on the test
        // name otherwise, so that a targetless test never reports errors at an arbitrary position.
        SyntaxBase IArtifactReferenceSyntax.SourceSyntax => Path ?? Name;

        /// <summary>
        /// The position to report target-related diagnostics at.
        /// </summary>
        public SyntaxBase TargetDiagnosticSyntax => Path ?? Name;

        /// <summary>
        /// The body-owned target selector object, if one was declared.
        /// </summary>
        public ObjectSyntax? TryGetMatchSelectorSyntax()
            => this.TryGetBody()?.TryGetPropertyByName(LanguageConstants.TestMatchPropertyName)?.Value as ObjectSyntax;

        /// <summary>
        /// The <c>match</c> property, if declared, regardless of whether its value is a valid object.
        /// </summary>
        public ObjectPropertySyntax? TryGetMatchProperty()
            => this.TryGetBody()?.TryGetPropertyByName(LanguageConstants.TestMatchPropertyName);

        /// <summary>
        /// The body-owned assertions object, if one was declared.
        /// </summary>
        public ObjectSyntax? TryGetAssertionsSyntax()
            => this.TryGetBody()?.TryGetPropertyByName(LanguageConstants.TestAssertionsPropertyName)?.Value as ObjectSyntax;

        /// <summary>
        /// The <c>assertions</c> property, if declared, regardless of whether its value is a valid object.
        /// </summary>
        public ObjectPropertySyntax? TryGetAssertionsProperty()
            => this.TryGetBody()?.TryGetPropertyByName(LanguageConstants.TestAssertionsPropertyName);

        /// <summary>
        /// The literal target path as a string syntax node, or null when the test has no literal path
        /// or the path could not be parsed.
        /// </summary>
        public StringSyntax? TryGetPath() => this.Path as StringSyntax;

        public ObjectSyntax? TryGetBody() =>
            this.Value switch
            {
                ObjectSyntax @object => @object,
                SkippedTriviaSyntax => null,

                // blocked by assert in the constructor
                _ => throw new NotImplementedException($"Unexpected type of test value '{this.Value.GetType().Name}'.")
            };

        public ObjectSyntax GetBody() =>
            this.TryGetBody() ?? throw new InvalidOperationException($"A valid test body is not available on this test due to errors. Use {nameof(TryGetBody)}() instead.");

        public ArtifactType GetArtifactType() => ArtifactType.Module;
    }
}
