// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.Highlighting;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Utils;
using Bicep.Testing.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.IntegrationTests
{
    /// <summary>
    /// Semantic highlighting for the test framework. These assert the tokens the language server
    /// actually hands the editor, which is what decides the colours a user sees; the TextMate
    /// grammar only paints until the server responds.
    /// </summary>
    [TestClass]
    public class TestFrameworkHighlightingTests
    {
        private ServiceBuilder ServicesWithTestFramework => new ServiceBuilder()
            .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true));

        [NotNull]
        public TestContext? TestContext { get; set; }

        private static ImmutableArray<(string Text, SemanticTokenType TokenType)> Tokenize(
            CompilationHelper.CompilationResult result,
            string sourceText)
            => [.. SemanticTokenVisitor.Build(result.Compilation.GetEntrypointSemanticModel())
                .Select(token => (
                    sourceText.Substring(token.Positionable.Span.Position, token.Positionable.Span.Length),
                    token.TokenType))];

        private ImmutableArray<(string Text, SemanticTokenType TokenType)> TokenizeTestFile(string testFileContents)
        {
            var fileSet = new MockFileSystemTestFileSet();
            fileSet.AddFile("sample.biceptest", testFileContents);
            fileSet.AddFile("target.bicep", "param name string = 'us'\n");

            var result = CompilationHelper.Compile(ServicesWithTestFramework, fileSet, fileSet.GetUri("sample.biceptest"));

            return Tokenize(result, testFileContents);
        }

        [TestMethod]
        public void An_assertion_name_is_highlighted_as_a_declaration_rather_than_a_property()
        {
            var tokens = TokenizeTestFile("""
                test policy = {
                  match: {
                    root: 'modules'
                  }
                  assertions: {
                    onlyAllowedResourceTypes: {
                      passWhen: length(target.resources) == 0
                      message: 'No resources allowed.'
                    }
                  }
                }
                """);

            // The name the author invented.
            tokens.Should().Contain(("onlyAllowedResourceTypes", SemanticTokenType.Variable),
                because: "an assertion name is declared by the author, not a fixed part of the language");

            // The fixed parts of the language around it keep the property highlighting every
            // other Bicep object key gets, so the two are visually distinguishable.
            tokens.Should().Contain(("match", SemanticTokenType.TypeParameter));
            tokens.Should().Contain(("assertions", SemanticTokenType.TypeParameter));
            tokens.Should().Contain(("passWhen", SemanticTokenType.TypeParameter));
            tokens.Should().Contain(("message", SemanticTokenType.TypeParameter));
        }

        [TestMethod]
        public void An_assertion_name_under_a_literal_target_is_highlighted_the_same_way()
        {
            var tokens = TokenizeTestFile("""
                test policy 'target.bicep' = {
                  params: {
                    name: 'eu'
                  }
                  assertions: {
                    declaresNothing: {
                      passWhen: length(target.resources) == 0
                      message: 'No resources allowed.'
                    }
                  }
                }
                """);

            tokens.Should().Contain(("declaresNothing", SemanticTokenType.Variable));
            tokens.Should().Contain(("params", SemanticTokenType.TypeParameter));
        }

        [TestMethod]
        public void A_property_named_assertions_outside_a_test_body_is_left_alone()
        {
            var tokens = TokenizeTestFile("""
                var notATest = {
                  assertions: {
                    looksLikeAnAssertion: {
                      message: 'but it is an ordinary object'
                    }
                  }
                }

                test policy = {
                  match: {
                    root: 'modules'
                  }
                  params: {
                    name: notATest.assertions.looksLikeAnAssertion.message
                  }
                }
                """);

            tokens.Should().Contain(("looksLikeAnAssertion", SemanticTokenType.TypeParameter),
                because: "only the assertions of a test declaration name author-declared checks");
            tokens.Should().NotContain(("looksLikeAnAssertion", SemanticTokenType.Variable));
        }

        [TestMethod]
        public void An_ordinary_bicep_object_key_is_unaffected()
        {
            var bicepText = """
                resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
                  name: 'example'
                }

                var tags = {
                  assertions: 'not a test'
                  environment: 'prod'
                }

                output combined object = union(tags, { id: storage.id })
                """;

            var result = CompilationHelper.Compile(ServicesWithTestFramework, bicepText);
            var tokens = Tokenize(result, bicepText);

            tokens.Should().Contain(("environment", SemanticTokenType.TypeParameter));
            tokens.Should().Contain(("assertions", SemanticTokenType.TypeParameter),
                because: "highlighting a plain Bicep object must not change because of the test framework");
            tokens.Should().NotContain(("assertions", SemanticTokenType.Variable));
        }

        [TestMethod]
        public void A_case_declaration_is_highlighted()
        {
            var testFileContents = """
                param name string

                test policy = {
                  match: {
                    root: 'modules'
                  }
                  params: {
                    name: name
                  }
                }
                """;

            var caseFileContents = """
                using 'sample.biceptest'

                case europe = {
                  name: 'eu'
                }
                """;

            var fileSet = new MockFileSystemTestFileSet();
            fileSet.AddFile("sample.biceptest", testFileContents);
            fileSet.AddFile("cases.biceptestparam", caseFileContents);
            fileSet.AddFile("target.bicep", "param name string = 'us'\n");

            var result = CompilationHelper.Compile(ServicesWithTestFramework, fileSet, fileSet.GetUri("cases.biceptestparam"));
            var tokens = Tokenize(result, caseFileContents);

            tokens.Should().Contain(("case", SemanticTokenType.Keyword),
                because: "'case' introduces a declaration, exactly as 'test' and 'param' do");
            tokens.Should().Contain(("europe", SemanticTokenType.Variable),
                because: "a case name is a declared name");
        }
    }
}
