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
    public class BicepTestFileTests
    {
        private static IOUri Uri(string path) => new("file", "", path);

        [TestMethod]
        public void CreateSourceFile_WithBicepTestExtension_CreatesBicepTestFile()
        {
            var sourceFile = BicepTestConstants.SourceFileFactory.CreateSourceFile(Uri("/foo/bar.biceptest"), "");

            sourceFile.Should().BeOfType<BicepTestFile>();
        }

        [TestMethod]
        public void CreateSourceFile_WithExplicitBicepTestFileType_CreatesBicepTestFile()
        {
            var sourceFile = BicepTestConstants.SourceFileFactory.CreateSourceFile(Uri("/foo/untitled"), "", typeof(BicepTestFile));

            sourceFile.Should().BeOfType<BicepTestFile>();
        }

        [TestMethod]
        public void BicepTestFile_HasTestFileKind()
        {
            var file = BicepTestConstants.SourceFileFactory.CreateBicepTestFile(Uri("/foo/bar.biceptest"), "");

            file.FileKind.Should().Be(BicepSourceFileKind.TestFile);
        }

        [TestMethod]
        public void BicepTestFile_ShallowClone_PreservesKindAndText()
        {
            var file = BicepTestConstants.SourceFileFactory.CreateBicepTestFile(Uri("/foo/bar.biceptest"), "param foo string\n");

            var clone = file.ShallowClone();

            clone.Should().BeOfType<BicepTestFile>();
            clone.FileKind.Should().Be(BicepSourceFileKind.TestFile);
            clone.Text.Should().Be(file.Text);
        }

        [TestMethod]
        public void BicepTestFile_ParsesTestDeclarations()
        {
            var file = BicepTestConstants.SourceFileFactory.CreateBicepTestFile(
                Uri("/foo/bar.biceptest"),
                """
                test myTest 'main.bicep' = {
                  params: {
                    foo: 'bar'
                  }
                }
                """);

            file.ProgramSyntax.Declarations.Should().ContainSingle();
            file.ProgramSyntax.Declarations.Single().Should().BeOfType<TestDeclarationSyntax>();
            file.ParsingErrorLookup.Should().BeEmpty();
        }

        [TestMethod]
        public void BicepTestExtension_IsRecognized()
        {
            Uri("/foo/bar.biceptest").HasBicepTestExtension().Should().BeTrue();
            Uri("/foo/bar.bicep").HasBicepTestExtension().Should().BeFalse();
            Uri("/foo/bar.bicepparam").HasBicepTestExtension().Should().BeFalse();
        }

        [TestMethod]
        public void BicepTestExtension_DoesNotCollideWithBicepExtension()
        {
            // ".biceptest" must not be treated as a ".bicep" file.
            Uri("/foo/bar.biceptest").HasBicepExtension().Should().BeFalse();
        }
    }
}
