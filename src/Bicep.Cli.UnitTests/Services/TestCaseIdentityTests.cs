// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Cli.Services;
using Bicep.IO.Abstraction;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.UnitTests.Services;

[TestClass]
public class TestCaseIdentityTests
{
    private static TestCaseIdentity Identity(string testFile, string testName, string targetFile)
        => new(IOUri.FromFilePath(testFile), testName, IOUri.FromFilePath(targetFile));

    [TestMethod]
    public void TestFileName_ReturnsFileNameWithoutDirectory()
    {
        Identity("/repo/tests/storage.biceptest", "validPrefix", "/repo/src/storage.bicep")
            .TestFileName.Should().Be("storage.biceptest");
    }

    [TestMethod]
    public void RelativeTargetPath_IsRelativeToTheTestFileDirectory()
    {
        Identity("/repo/tests/storage.biceptest", "validPrefix", "/repo/tests/storage.bicep")
            .RelativeTargetPath.Should().Be("storage.bicep");

        Identity("/repo/tests/storage.biceptest", "validPrefix", "/repo/src/modules/storage.bicep")
            .RelativeTargetPath.Should().Be("../src/modules/storage.bicep");
    }

    [TestMethod]
    public void CaseId_CombinesTestFileNameTestNameAndRelativeTarget()
    {
        Identity("/repo/tests/storage.biceptest", "validPrefix", "/repo/src/storage.bicep")
            .CaseId.Should().Be("storage.biceptest#validPrefix#../src/storage.bicep");
    }

    [TestMethod]
    public void CaseId_IsUnaffectedByTheLocationOfTheRepository()
    {
        // The same test and target laid out identically under two different roots must produce the
        // same case id, so that running from a different working directory or clone does not change identities.
        var first = Identity("/repo-a/tests/storage.biceptest", "validPrefix", "/repo-a/src/storage.bicep");
        var second = Identity("/somewhere/else/repo-b/tests/storage.biceptest", "validPrefix", "/somewhere/else/repo-b/src/storage.bicep");

        second.CaseId.Should().Be(first.CaseId);
    }

    [TestMethod]
    public void CaseId_DistinguishesTargetsOfTheSameTest()
    {
        var first = Identity("/repo/tests/policy.biceptest", "approved", "/repo/src/a.bicep");
        var second = Identity("/repo/tests/policy.biceptest", "approved", "/repo/src/b.bicep");

        first.CaseId.Should().NotBe(second.CaseId);
    }

    [TestMethod]
    public void CaseId_DistinguishesTestsSharingATarget()
    {
        var first = Identity("/repo/tests/policy.biceptest", "approved", "/repo/src/a.bicep");
        var second = Identity("/repo/tests/policy.biceptest", "naming", "/repo/src/a.bicep");

        first.CaseId.Should().NotBe(second.CaseId);
    }

    [TestMethod]
    public void Equality_IsStructural()
    {
        Identity("/repo/tests/a.biceptest", "t", "/repo/src/a.bicep")
            .Should().Be(Identity("/repo/tests/a.biceptest", "t", "/repo/src/a.bicep"));

        Identity("/repo/tests/a.biceptest", "t", "/repo/src/a.bicep")
            .Should().NotBe(Identity("/repo/tests/a.biceptest", "t", "/repo/src/b.bicep"));
    }

    [TestMethod]
    public void CompareTo_OrdersByTestFileThenTestNameThenTarget()
    {
        var unordered = new[]
        {
            Identity("/repo/tests/b.biceptest", "alpha", "/repo/src/a.bicep"),
            Identity("/repo/tests/a.biceptest", "beta", "/repo/src/a.bicep"),
            Identity("/repo/tests/a.biceptest", "alpha", "/repo/src/b.bicep"),
            Identity("/repo/tests/a.biceptest", "alpha", "/repo/src/a.bicep"),
        };

        var ordered = unordered.Order().Select(x => x.CaseId).ToArray();

        ordered.Should().Equal(
            "a.biceptest#alpha#../src/a.bicep",
            "a.biceptest#alpha#../src/b.bicep",
            "a.biceptest#beta#../src/a.bicep",
            "b.biceptest#alpha#../src/a.bicep");
    }

    [TestMethod]
    public void CompareTo_OrdersIdenticallyOnEveryHost()
    {
        // Ordinal and case-insensitive ordering disagree about these names. Pinning the ordinal
        // answer keeps a suite's reported case order the same on Windows and in a Linux pipeline,
        // so two runs of the same suite stay comparable across hosts.
        var unordered = new[]
        {
            Identity("/repo/tests/app.biceptest", "t", "/repo/src/a.bicep"),
            Identity("/repo/tests/README.biceptest", "t", "/repo/src/a.bicep"),
            Identity("/repo/tests/Zoo.biceptest", "t", "/repo/src/a.bicep"),
        };

        unordered.Order().Select(x => x.TestFileName)
            .Should().Equal("README.biceptest", "Zoo.biceptest", "app.biceptest");
    }

    [TestMethod]
    public void CompareTo_OrdersTargetsOfOneTestIdenticallyOnEveryHost()
    {
        var unordered = new[]
        {
            Identity("/repo/tests/a.biceptest", "t", "/repo/src/app.bicep"),
            Identity("/repo/tests/a.biceptest", "t", "/repo/src/README.bicep"),
            Identity("/repo/tests/a.biceptest", "t", "/repo/src/Zoo.bicep"),
        };

        unordered.Order().Select(x => x.RelativeTargetPath)
            .Should().Equal("../src/README.bicep", "../src/Zoo.bicep", "../src/app.bicep");
    }

    [TestMethod]
    public void CompareTo_IsConsistentWithEqualityForPathsNamingOneFile()
    {
        // Two spellings of one path must not order as distinct while comparing as equal. On a
        // case-insensitive host these name the same file and compare equal; on a case-sensitive one
        // they are different files and order deterministically.
        var lower = Identity("/repo/tests/a.biceptest", "t", "/repo/src/storage.bicep");
        var upper = Identity("/repo/tests/a.biceptest", "t", "/repo/src/STORAGE.bicep");

        (lower.CompareTo(upper) == 0).Should().Be(lower.Equals(upper));
    }

    [TestMethod]
    public void CompareTo_SortsNullFirst()
    {
        Identity("/repo/tests/a.biceptest", "t", "/repo/src/a.bicep")
            .CompareTo(null).Should().BePositive();
    }
}
