// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Syntax;
using Bicep.Core.TestFramework;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.TestFramework;

[TestClass]
public class TestTargetSelectorBinderTests
{
    [TestMethod]
    public void TryBind_returns_null_when_the_test_declares_no_selector()
    {
        var test = ParseTest("test foo = {\n  params: {}\n}");

        TestTargetSelectorBinder.TryBind(test).Should().BeNull();
    }

    [TestMethod]
    public void TryBind_reads_every_declared_property()
    {
        var test = ParseTest("test foo = {\n  match: {\n    root: 'modules'\n    include: ['a/*.bicep', 'b/*.bicep']\n    exclude: ['b/skip.bicep']\n    allowEmpty: true\n  }\n}");

        var selector = TestTargetSelectorBinder.TryBind(test);

        selector.Should().NotBeNull();
        selector!.Root.Should().Be("modules");
        selector.Include.Should().Equal("a/*.bicep", "b/*.bicep");
        selector.Exclude.Should().Equal("b/skip.bicep");
        selector.AllowEmpty.Should().BeTrue();
    }

    [TestMethod]
    public void TryBind_applies_defaults_for_omitted_properties()
    {
        var test = ParseTest("test foo = {\n  match: {\n    include: ['*.bicep']\n  }\n}");

        var selector = TestTargetSelectorBinder.TryBind(test);

        selector.Should().NotBeNull();
        selector!.Root.Should().Be(TestTargetSelector.DefaultRoot);
        selector.Exclude.Should().BeEmpty();
        selector.AllowEmpty.Should().BeFalse();
    }

    [TestMethod]
    public void TryBind_skips_non_literal_patterns_rather_than_guessing_at_them()
    {
        // A non-literal value has already been reported as a type error. Binding must not invent a value for it.
        var test = ParseTest("test foo = {\n  match: {\n    include: ['*.bicep', someVar]\n  }\n}");

        var selector = TestTargetSelectorBinder.TryBind(test);

        selector.Should().NotBeNull();
        selector!.Include.Should().Equal("*.bicep");
    }

    [TestMethod]
    public void TryBind_returns_an_empty_include_list_when_the_value_is_not_an_array()
    {
        var test = ParseTest("test foo = {\n  match: {\n    include: '*.bicep'\n  }\n}");

        var selector = TestTargetSelectorBinder.TryBind(test);

        selector.Should().NotBeNull();
        selector!.Include.Should().BeEmpty();
    }

    [DataTestMethod]
    [DataRow("../*.bicep")]
    [DataRow("..\\*.bicep")]
    [DataRow("a/../../b.bicep")]
    [DataRow("/rooted.bicep")]
    [DataRow("C:\\rooted.bicep")]
    public void PatternEscapesRoot_rejects_patterns_that_leave_the_root(string pattern)
        => TestTargetSelector.PatternEscapesRoot(pattern).Should().BeTrue();

    [DataTestMethod]
    [DataRow("*.bicep")]
    [DataRow("a/b/*.bicep")]
    [DataRow("**/*.bicep")]
    [DataRow("a\\b\\*.bicep")]
    [DataRow("..hidden/*.bicep")]
    public void PatternEscapesRoot_accepts_patterns_contained_by_the_root(string pattern)
        => TestTargetSelector.PatternEscapesRoot(pattern).Should().BeFalse();

    private static TestDeclarationSyntax ParseTest(string text)
    {
        var program = ParserHelper.Parse(text);

        return program.Declarations.OfType<TestDeclarationSyntax>().Single();
    }
}
