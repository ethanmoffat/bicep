// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Extensions;
using Bicep.Core.SourceGraph;
using Bicep.Core.Syntax;
using Bicep.Core.UnitTests;
using Bicep.IO.Abstraction;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.SourceGraph
{
    [TestClass]
    public class BicepTestParamFileTests
    {
        private static IOUri Uri(string path) => new("file", "", path);

        private static BicepTestParamFile Create(string contents) =>
            BicepTestConstants.SourceFileFactory.CreateBicepTestParamFile(Uri("/foo/bar.biceptestparam"), contents);

        [TestMethod]
        public void CreateSourceFile_WithBicepTestParamsExtension_CreatesBicepTestParamFile()
        {
            var sourceFile = BicepTestConstants.SourceFileFactory.CreateSourceFile(Uri("/foo/bar.biceptestparam"), "");

            sourceFile.Should().BeOfType<BicepTestParamFile>();
        }

        [TestMethod]
        public void CreateSourceFile_WithExplicitBicepTestParamFileType_CreatesBicepTestParamFile()
        {
            var sourceFile = BicepTestConstants.SourceFileFactory.CreateSourceFile(Uri("/foo/untitled"), "", typeof(BicepTestParamFile));

            sourceFile.Should().BeOfType<BicepTestParamFile>();
        }

        [TestMethod]
        public void BicepTestParamFile_HasTestParamsFileKind()
        {
            Create("").FileKind.Should().Be(BicepSourceFileKind.TestParamsFile);
        }

        [TestMethod]
        public void BicepTestParamFile_ShallowClone_PreservesKindAndText()
        {
            var file = Create("using 'main.biceptest'\n");

            var clone = file.ShallowClone();

            clone.Should().BeOfType<BicepTestParamFile>();
            clone.FileKind.Should().Be(BicepSourceFileKind.TestParamsFile);
            clone.Text.Should().Be(file.Text);
        }

        [TestMethod]
        public void BicepTestParamsExtension_IsRecognized()
        {
            Uri("/foo/bar.biceptestparam").HasBicepTestParamsExtension().Should().BeTrue();
            Uri("/foo/bar.biceptest").HasBicepTestParamsExtension().Should().BeFalse();
            Uri("/foo/bar.bicepparam").HasBicepTestParamsExtension().Should().BeFalse();
        }

        [TestMethod]
        public void BicepTestParamsExtension_DoesNotCollideWithOtherExtensions()
        {
            var uri = Uri("/foo/bar.biceptestparam");

            uri.HasBicepExtension().Should().BeFalse();
            uri.HasBicepParamExtension().Should().BeFalse();
            uri.HasBicepTestExtension().Should().BeFalse();
        }

        [TestMethod]
        public void Parser_ParsesUsingCasesAndDeploymentContext()
        {
            var file = Create(
                """
                using 'app.biceptest'

                deploymentContext = {
                  subscriptionId: '00000000-0000-0000-0000-000000000000'
                }

                case westus = {
                  location: 'westus'
                }

                case eastus = {
                  location: 'eastus'
                }
                """);

            file.ParsingErrorLookup.Should().BeEmpty();
            file.ProgramSyntax.Declarations.Should().SatisfyRespectively(
                x => x.Should().BeOfType<UsingDeclarationSyntax>(),
                x => x.Should().BeOfType<DeploymentContextDeclarationSyntax>(),
                x => x.Should().BeOfType<TestCaseDeclarationSyntax>(),
                x => x.Should().BeOfType<TestCaseDeclarationSyntax>());
        }

        [TestMethod]
        public void Parser_ExposesCaseNameAndBody()
        {
            var file = Create(
                """
                using 'app.biceptest'

                case westus = {
                  location: 'westus'
                }
                """);

            var caseSyntax = file.ProgramSyntax.Declarations.OfType<TestCaseDeclarationSyntax>().Single();

            caseSyntax.Name.IdentifierName.Should().Be("westus");
            caseSyntax.Body.Should().NotBeNull();
            caseSyntax.Body!.Properties.Should().ContainSingle();
        }

        [TestMethod]
        public void Parser_SupportsVariablesAndDecoratedCases()
        {
            var file = Create(
                """
                using 'app.biceptest'

                var sharedPrefix = 'contoso'

                @resourceGroup('rg-westus')
                case westus = {
                  name: '${sharedPrefix}-westus'
                }
                """);

            file.ParsingErrorLookup.Should().BeEmpty();

            var caseSyntax = file.ProgramSyntax.Declarations.OfType<TestCaseDeclarationSyntax>().Single();
            caseSyntax.Decorators.Should().ContainSingle();
        }

        [TestMethod]
        public void Parser_RejectsUnsupportedDeclarations()
        {
            var file = Create(
                """
                using 'app.biceptest'

                resource foo 'Microsoft.Storage/storageAccounts@2022-09-01' = {
                  name: 'foo'
                }
                """);

            file.ParsingErrorLookup.Should().NotBeEmpty();
            file.ParsingErrorLookup.Select(x => x.Code).Should().Contain("BCP464");
        }

        [TestMethod]
        public void Parser_RequiresACaseIdentifier()
        {
            var file = Create(
                """
                using 'app.biceptest'

                case = {
                }
                """);

            file.ParsingErrorLookup.Select(x => x.Code).Should().Contain("BCP465");
        }
    }
}
