// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Syntax;

namespace Bicep.Core.Parsing
{
    /// <summary>
    /// Parses a test parameters file: a "using" reference to the test file it supplies inputs for,
    /// an optional file-level deployment context, and one or more named input cases.
    ///
    /// Cases are ordinary Bicep objects, so expression parsing, typing and defaults are the ones
    /// authors already know rather than a separate data language.
    /// </summary>
    public class TestParamsParser : BaseParser
    {
        public TestParamsParser(string text) : base(text)
        {
        }

        public override ProgramSyntax Program()
        {
            var declarationsOrTokens = new List<SyntaxBase>();

            while (!this.IsAtEnd())
            {
                var declarationOrToken = Declaration();
                declarationsOrTokens.Add(declarationOrToken);

                if (declarationOrToken is StatementSyntax)
                {
                    var newLine = this.WithRecoveryNullable(this.NewLineOrEof, RecoveryFlags.ConsumeTerminator, TokenType.NewLine);

                    if (newLine != null)
                    {
                        declarationsOrTokens.Add(newLine);
                    }
                }
            }

            var endOfFile = reader.Read();
            var programSyntax = new ProgramSyntax(declarationsOrTokens, endOfFile);

            var parsingErrorVisitor = new ParseDiagnosticsVisitor(this.ParsingErrorTree);
            parsingErrorVisitor.Visit(programSyntax);

            return programSyntax;
        }

        protected override SyntaxBase Declaration(params string[] expectedKeywords) =>
            this.WithRecovery(
                () =>
                {
                    var leadingNodes = DecorableSyntaxLeadingNodes().ToImmutableArray();

                    var current = reader.Peek();

                    return current.Type switch
                    {
                        TokenType.Identifier => ValidateKeyword(current.Text) switch
                        {
                            LanguageConstants.UsingKeyword => this.UsingDeclaration(leadingNodes),
                            LanguageConstants.TestCaseKeyword => this.TestCaseDeclaration(leadingNodes),
                            LanguageConstants.DeploymentContextKeyword => this.DeploymentContextDeclaration(leadingNodes),
                            LanguageConstants.VariableKeyword => this.VariableDeclaration(leadingNodes),
                            _ => throw new ExpectedTokenException(current, b => b.UnrecognizedTestParamsFileDeclaration()),
                        },
                        TokenType.NewLine => this.NewLine(),
                        _ => throw new ExpectedTokenException(current, b => b.UnrecognizedTestParamsFileDeclaration()),
                    };

                    string? ValidateKeyword(string keyword) =>
                        expectedKeywords.Length == 0 || expectedKeywords.Contains(keyword) ? keyword : null;
                },
                RecoveryFlags.None,
                TokenType.NewLine);

        private UsingDeclarationSyntax UsingDeclaration(ImmutableArray<SyntaxBase> leadingNodes)
        {
            var keyword = ExpectKeyword(LanguageConstants.UsingKeyword);

            var path = this.WithRecovery(
                () => ThrowIfSkipped(this.InterpolableString, b => b.ExpectedFilePathString()),
                RecoveryFlags.None,
                TokenType.NewLine);

            return new(leadingNodes, keyword, path, this.SkipEmpty());
        }

        private TestCaseDeclarationSyntax TestCaseDeclaration(ImmutableArray<SyntaxBase> leadingNodes)
        {
            var keyword = ExpectKeyword(LanguageConstants.TestCaseKeyword);
            var name = this.IdentifierWithRecovery(b => b.ExpectedTestCaseIdentifier(), RecoveryFlags.None, TokenType.Identifier, TokenType.NewLine);
            var assignment = this.WithRecovery(this.Assignment, GetSuppressionFlag(name), TokenType.NewLine);
            var value = this.WithRecovery(() => this.Object(ExpressionFlags.AllowComplexLiterals), GetSuppressionFlag(assignment), TokenType.NewLine);

            return new(leadingNodes, keyword, name, assignment, value);
        }

        private DeploymentContextDeclarationSyntax DeploymentContextDeclaration(ImmutableArray<SyntaxBase> leadingNodes)
        {
            var keyword = ExpectKeyword(LanguageConstants.DeploymentContextKeyword);
            var assignment = this.WithRecovery(this.Assignment, RecoveryFlags.None, TokenType.NewLine);
            var value = this.WithRecovery(() => this.Object(ExpressionFlags.AllowComplexLiterals), GetSuppressionFlag(assignment), TokenType.NewLine);

            return new(leadingNodes, keyword, assignment, value);
        }
    }
}
