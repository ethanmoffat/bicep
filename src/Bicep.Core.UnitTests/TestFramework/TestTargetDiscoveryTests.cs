// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TestFramework;
using Bicep.IO.Abstraction;
using Bicep.IO.InMemory;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.TestFramework;

[TestClass]
public class TestTargetDiscoveryTests
{
    private const string TestFileDirectory = "/repo/tests";

    private static IDirectoryHandle CreateTree(params string[] filePaths)
    {
        var fileExplorer = new InMemoryFileExplorer();

        foreach (var filePath in filePaths)
        {
            var fileUri = IOUri.FromFilePath(filePath);

            // The in-memory store does not create ancestors implicitly, so materialize every
            // directory on the way down.
            var segments = fileUri.PathSegments;
            var directoryPath = "";

            foreach (var segment in segments[..^1])
            {
                directoryPath += $"/{segment}";
                fileExplorer.GetDirectory(IOUri.FromFilePath(directoryPath)).EnsureExists();
            }

            fileExplorer.GetFile(fileUri).WriteAllText("// file");
        }

        return fileExplorer.GetDirectory(IOUri.FromFilePath(TestFileDirectory));
    }

    private static string[] Discover(IDirectoryHandle testFileDirectory, TestTargetSelector selector)
    {
        var result = TestTargetDiscovery.Discover(testFileDirectory, selector);

        result.Error.Should().BeNull();

        return [.. result.Targets.Select(x => x.GetFilePath().Replace('\\', '/'))];
    }

    [TestMethod]
    public void Discover_WithDefaultRoot_SelectsFilesBesideTheTestFile()
    {
        var tree = CreateTree(
            "/repo/tests/a.bicep",
            "/repo/tests/b.bicep",
            "/repo/src/c.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["*.bicep"]))
            .Should().Equal("/repo/tests/a.bicep", "/repo/tests/b.bicep");
    }

    [TestMethod]
    public void Discover_WithRelativeRoot_AnchorsPatternsToThatRoot()
    {
        var tree = CreateTree(
            "/repo/tests/a.bicep",
            "/repo/src/b.bicep",
            "/repo/src/nested/c.bicep");

        Discover(tree, TestTargetSelector.Create("../src", ["**/*.bicep"]))
            .Should().Equal("/repo/src/b.bicep", "/repo/src/nested/c.bicep");
    }

    [TestMethod]
    public void Discover_RecursesIntoNestedDirectories()
    {
        var tree = CreateTree(
            "/repo/tests/a.bicep",
            "/repo/tests/one/b.bicep",
            "/repo/tests/one/two/c.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .Should().Equal("/repo/tests/a.bicep", "/repo/tests/one/b.bicep", "/repo/tests/one/two/c.bicep");
    }

    [TestMethod]
    public void Discover_OnlyMatchesTheRequestedExtension()
    {
        var tree = CreateTree(
            "/repo/tests/a.bicep",
            "/repo/tests/a.biceptest",
            "/repo/tests/a.json");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .Should().Equal("/repo/tests/a.bicep");
    }

    [TestMethod]
    public void Discover_ExcludesWinOverIncludes()
    {
        var tree = CreateTree(
            "/repo/tests/keep.bicep",
            "/repo/tests/generated/drop.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"], ["generated/**"]))
            .Should().Equal("/repo/tests/keep.bicep");
    }

    [TestMethod]
    public void Discover_OverlappingIncludesProduceNoDuplicates()
    {
        var tree = CreateTree("/repo/tests/a.bicep", "/repo/tests/b.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["*.bicep", "**/*.bicep", "a.bicep"]))
            .Should().Equal("/repo/tests/a.bicep", "/repo/tests/b.bicep");
    }

    [TestMethod]
    public void Discover_OrdersResultsDeterministically()
    {
        // Declared out of order to confirm the result is sorted rather than filesystem-ordered.
        var tree = CreateTree(
            "/repo/tests/z.bicep",
            "/repo/tests/m/b.bicep",
            "/repo/tests/a.bicep",
            "/repo/tests/m/a.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .Should().Equal(
                "/repo/tests/a.bicep",
                "/repo/tests/m/a.bicep",
                "/repo/tests/m/b.bicep",
                "/repo/tests/z.bicep");
    }

    [TestMethod]
    public void Discover_ExclusionsDoNotLeakBetweenSelectors()
    {
        var tree = CreateTree("/repo/tests/a.bicep", "/repo/tests/b.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"], ["b.bicep"]))
            .Should().Equal("/repo/tests/a.bicep");

        // A second selector over the same tree still sees the file the first one excluded.
        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .Should().Equal("/repo/tests/a.bicep", "/repo/tests/b.bicep");
    }

    [TestMethod]
    public void Discover_MatchesWholeSegmentsRatherThanPrefixes()
    {
        var tree = CreateTree(
            "/repo/tests/src/a.bicep",
            "/repo/tests/src-generated/b.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["src/**/*.bicep"]))
            .Should().Equal("/repo/tests/src/a.bicep");
    }

    [TestMethod]
    public void Discover_AcceptsBackslashSeparatorsInPatterns()
    {
        var tree = CreateTree("/repo/tests/src/a.bicep");

        Discover(tree, TestTargetSelector.Create(null, [@"src\*.bicep"]))
            .Should().Equal("/repo/tests/src/a.bicep");
    }

    [TestMethod]
    public void Discover_WithNoIncludePatterns_Fails()
    {
        var tree = CreateTree("/repo/tests/a.bicep");

        TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, []))
            .Error!.Kind.Should().Be(TestTargetDiscoveryErrorKind.NoIncludePatterns);
    }

    [DataTestMethod]
    [DataRow("../outside/*.bicep")]
    [DataRow("nested/../../*.bicep")]
    [DataRow("/absolute/*.bicep")]
    public void Discover_WithPatternEscapingTheRoot_Fails(string pattern)
    {
        var tree = CreateTree("/repo/tests/a.bicep", "/repo/outside/b.bicep");

        TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, [pattern]))
            .Error!.Kind.Should().Be(TestTargetDiscoveryErrorKind.PatternEscapesRoot);
    }

    [TestMethod]
    public void Discover_WithExcludeEscapingTheRoot_Fails()
    {
        var tree = CreateTree("/repo/tests/a.bicep");

        TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"], ["../*.bicep"]))
            .Error!.Kind.Should().Be(TestTargetDiscoveryErrorKind.PatternEscapesRoot);
    }

    [TestMethod]
    public void Discover_WithMissingRoot_Fails()
    {
        var tree = CreateTree("/repo/tests/a.bicep");

        var result = TestTargetDiscovery.Discover(tree, TestTargetSelector.Create("../does-not-exist", ["**/*.bicep"]));

        result.Error!.Kind.Should().Be(TestTargetDiscoveryErrorKind.RootNotFound);
        result.Targets.Should().BeEmpty();
    }

    [TestMethod]
    public void Discover_WithNoMatches_FailsByDefault()
    {
        var tree = CreateTree("/repo/tests/a.bicep");

        var result = TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, ["*.nomatch"]));

        result.Error!.Kind.Should().Be(TestTargetDiscoveryErrorKind.NoTargetsMatched);
        result.Error.Message.Should().Contain(TestTargetSelector.AllowEmptyPropertyName);
    }

    [TestMethod]
    public void Discover_WithNoMatches_SucceedsWhenExplicitlyAllowed()
    {
        var tree = CreateTree("/repo/tests/a.bicep");

        var result = TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, ["*.nomatch"], allowEmpty: true));

        result.IsSuccess.Should().BeTrue();
        result.Targets.Should().BeEmpty();
    }

    [TestMethod]
    public void Discover_DoesNotReachOutsideTheRoot()
    {
        var tree = CreateTree(
            "/repo/tests/inside.bicep",
            "/repo/outside.bicep",
            "/outside-entirely.bicep");

        Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .Should().Equal("/repo/tests/inside.bicep");
    }

    [TestMethod]
    public void Discover_ReportsNoSkippedDirectoriesForAnOrdinaryTree()
    {
        var tree = CreateTree("/repo/tests/a.bicep", "/repo/tests/nested/b.bicep");

        TestTargetDiscovery.Discover(tree, TestTargetSelector.Create(null, ["**/*.bicep"]))
            .SkippedDirectories.Should().BeEmpty();
    }
}
