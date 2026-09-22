// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.FileSystem;
using Bicep.Core.UnitTests.Utils;
using Bicep.IO.FileSystem;
using Bicep.LangServer.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Bicep.LangServer.IntegrationTests;

/// <summary>
/// End-to-end coverage that the language server actually serves a '.biceptest' document.
///
/// These tests exist because every editor feature silently did nothing for test-framework files: the
/// document reached the server, but no compilation was ever created for it, so each handler looked up a
/// context, found none and returned an empty result. Nothing threw, so nothing was logged either.
/// Asserting on a handler's output is what catches that; asserting on registration would not have.
/// </summary>
[TestClass]
public class TestFrameworkLanguageFeaturesTests
{
    [NotNull]
    public TestContext? TestContext { get; set; }

    private const string TargetBicep = @"
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: 'stgexample'
}
";

    [TestMethod]
    public async Task Opening_a_test_file_publishes_diagnostics()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");

        // StartServerWithText waits for diagnostics to be published, so it cannot return at all unless a
        // compilation was created for the document.
        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = @"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: filter(target.resources, r => r.existing)
      message: 'Targets must create the resources they own.'
    }
  }
}
",
            },
            testUri);

        helper.Server.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Test_file_reports_a_misspelled_target_fact()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var mainUri = InMemoryFileResolver.GetFileUri("/path/to/main.bicep");

        var fileResolver = new InMemoryFileResolver(new Dictionary<Uri, string>
        {
            [mainUri] = TargetBicep,
        });

        using var helper = await MultiFileLanguageServerHelper.StartLanguageServer(
            TestContext,
            services => services
                .WithFileExplorer(new FileSystemFileExplorer(fileResolver.MockFileSystem))
                .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));

        var diagnostics = await helper.OpenFileOnceAsync(TestContext, @"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: filter(target.resourcez, r => r.existing)
      message: 'Targets must create the resources they own.'
    }
  }
}
", testUri);

        diagnostics.Diagnostics.Should().Contain(
            d => d.Code!.Value.String == "BCP083",
            "because 'target' declares no additional properties, so a misspelled fact is an unknown property rather than a silently-passing null");
    }

    [TestMethod]
    public async Task Completions_inside_a_test_assertion_offer_target_facts()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: target.|
      message: 'Targets must create the resources they own.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        completions.Select(c => c.Label).Should().Contain(
            ["resources", "modules", "imports", "withModules", "evaluated"],
            "because the compiler-provided 'target' symbol is in scope inside an assertion body");
    }

    [TestMethod]
    public async Task Completions_inside_an_assertion_body_offer_its_properties()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      |
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        completions.Select(c => c.Label).Should().Contain(
            ["passWhen", "failOn", "message"],
            "because an assertion body is a typed object, so its properties must be offered");
    }

    [TestMethod]
    public async Task Completions_inside_the_match_selector_offer_its_properties()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  match: {
    |
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        completions.Select(c => c.Label).Should().Contain(
            ["root", "include", "exclude"],
            "because the match selector is a typed object, so its properties must be offered");
    }

    [TestMethod]
    public async Task Completions_in_a_test_body_offer_the_test_properties()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  |
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        completions.Select(c => c.Label).Should().Contain(
            ["match", "assertions", "params"],
            "because a test body is a typed object, so its properties must be offered");
    }

    [TestMethod]
    public async Task Hovering_the_target_symbol_describes_it()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: filter(ta|rget.resources, r => r.existing)
      message: 'Targets must create the resources they own.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var hover = await file.RequestHover(cursor);

        hover.Should().NotBeNull("because 'target' is a symbol, so hovering it must produce something");
        hover!.Contents.MarkupContent!.Value.Should().Contain("running against");
    }

    [TestMethod]
    public async Task Hovering_an_input_case_names_it()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var paramsUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptestparam");

        var testText = @"
param namePrefix string

test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  params: {
    namePrefix: namePrefix
  }
}
";
        var (paramsText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
using './policy.biceptest'

case shor|tPrefix = {
  namePrefix: 'contoso'
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
                [paramsUri] = paramsText,
            },
            paramsUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(paramsUri, paramsText));
        var hover = await file.RequestHover(cursor);

        hover.Should().NotBeNull("because a case is a declared symbol, so hovering it must produce something");
        hover!.Contents.MarkupContent!.Value.Should().Contain("case shortPrefix");
    }

    [TestMethod]
    public async Task Completions_in_a_test_body_carry_property_descriptions()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  |
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        // Property keys deliberately do not hover, so completion is the only place these descriptions
        // can reach an author.
        foreach (var label in new[] { "match", "assertions", "params" })
        {
            var item = completions.Should().ContainSingle(c => c.Label == label).Subject;

            item.Documentation?.MarkupContent?.Value.Should().NotBeNullOrWhiteSpace(
                $"because the '{label}' property declares a description");
        }
    }

    [TestMethod]
    public async Task Completions_after_target_evaluated_offer_the_evaluated_facts()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: target.evaluated.|
      message: 'Targets must create the resources they own.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        completions.Select(c => c.Label).Should().Contain(
            ["resources", "outputs", "withModules"],
            "because 'evaluated' carries what the selected file would produce for this case");
    }

    [TestMethod]
    public async Task The_target_symbol_is_not_in_scope_outside_an_assertion()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
var namePrefix = 'contoso'

test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  params: {
    prefix: |
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var completions = await file.RequestAndResolveCompletions(cursor);

        var labels = completions.Select(c => c.Label).ToList();

        // The guard: ordinary symbols do complete here, so an empty list cannot make this pass.
        labels.Should().Contain("namePrefix");
        labels.Should().NotContain(
            "target",
            "because 'target' describes the file under test, and an input value is chosen before one is selected");
    }

    [TestMethod]
    public async Task Test_file_reports_an_assertion_that_declares_both_checks()
    {
        var diagnostics = await OpenTestFileForDiagnostics(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    cannotBeBoth: {
      passWhen: true
      failOn: []
      message: 'an assertion cannot be phrased two ways at once'
    }
  }
}
");

        diagnostics.Diagnostics.Should().Contain(
            d => d.Code!.Value.String == "BCP463",
            "because an assertion uses exactly one of 'passWhen' or 'failOn'");
    }

    [TestMethod]
    public async Task Test_file_reports_an_assertion_that_checks_nothing()
    {
        var diagnostics = await OpenTestFileForDiagnostics(@"
test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    checksNothing: {}
  }
}
");

        var codes = diagnostics.Diagnostics.Select(d => d.Code!.Value.String).ToList();

        // An assertion that checks nothing passes vacuously, which is the failure mode the framework
        // exists to prevent, so it is reported twice over: no check, and no message.
        codes.Should().Contain("BCP463");
        codes.Should().Contain("BCP035");
    }

    [TestMethod]
    public async Task Correcting_a_test_file_clears_its_diagnostics_without_saving()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var mainUri = InMemoryFileResolver.GetFileUri("/path/to/main.bicep");

        var fileResolver = new InMemoryFileResolver(new Dictionary<Uri, string>
        {
            [mainUri] = TargetBicep,
        });

        using var helper = await MultiFileLanguageServerHelper.StartLanguageServer(
            TestContext,
            services => services
                .WithFileExplorer(new FileSystemFileExplorer(fileResolver.MockFileSystem))
                .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));

        const string TestFileFormat = @"
test sourcePolicy = {{
  match: {{
    include: ['main.bicep']
  }}
  assertions: {{
    noExistingResources: {{
      failOn: filter(target.{0}, r => r.existing)
      message: 'Targets must create the resources they own.'
    }}
  }}
}}
";

        var withTypo = await helper.OpenFileOnceAsync(TestContext, string.Format(TestFileFormat, "resourcez"), testUri);
        withTypo.Diagnostics.Should().Contain(d => d.Code!.Value.String == "BCP083");

        var corrected = await helper.ChangeFileAsync(TestContext, string.Format(TestFileFormat, "resources"), testUri, 1);

        corrected.Diagnostics.Should().BeEmpty(
            "because the server recompiles the in-memory document, so a fix takes effect before the file is saved");
    }

    [TestMethod]
    public async Task Formatting_is_supported_for_test_files()
    {
        using var server = await MultiFileLanguageServerHelper.StartLanguageServer(
            TestContext,
            services => services.WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));

        var helper = new ServerRequestHelper(TestContext, server);

        await helper.OpenFile("/main.bicep", TargetBicep);

        var file = await helper.OpenFile("/policy.biceptest", """

            test    sourcePolicy = {
                match: {
              include: ['main.bicep']
                }
                  assertions: {
                noExistingResources:     {
                        failOn: filter(target.resources, r => r.existing)
                  message:   'Targets must create the resources they own.'
                }
              }
            }
            """);

        var textEdit = await file.Format();

        textEdit.NewText.Should().Be("""
            test sourcePolicy = {
              match: {
                include: ['main.bicep']
              }
              assertions: {
                noExistingResources: {
                  failOn: filter(target.resources, r => r.existing)
                  message: 'Targets must create the resources they own.'
                }
              }
            }

            """);
    }

    [TestMethod]
    public async Task Go_to_definition_from_an_assertion_finds_the_variable()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
var allowedTypes = ['Microsoft.Storage/storageAccounts']

test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    onlyAllowedTypes: {
      failOn: filter(target.resources, r => !contains(allowed|Types, r.type))
      message: 'Only the allowed resource types may be declared.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var clientFile = new LanguageClientFile(testUri, testText);
        var file = new FileRequestHelper(helper.Client, clientFile);
        var link = await file.GotoDefinition(cursor);

        link.TargetUri.ToString().Should().BeEquivalentTo(testUri.ToString());
        clientFile.GetOffset(link.TargetSelectionRange.Start).Should().Be(
            testText.IndexOf("allowedTypes"),
            "because the definition of a variable referenced from an assertion is its declaration");
    }

    [TestMethod]
    public async Task Hovering_a_variable_in_a_test_file_shows_its_type()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
var allowedTypes = ['Microsoft.Storage/storageAccounts']

test sourcePolicy = {
  match: {
    include: ['main.bicep']
  }
  assertions: {
    onlyAllowedTypes: {
      failOn: filter(target.resources, r => !contains(allowed|Types, r.type))
      message: 'Only the allowed resource types may be declared.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var hover = await file.RequestHover(cursor);

        hover.Should().NotBeNull();
        hover!.Contents.MarkupContent!.Value.Should().Contain("var allowedTypes");
    }

    [TestMethod]
    public async Task Hovering_a_test_property_key_describes_it()
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var (testText, cursor) = ParserHelper.GetFileWithSingleCursor(@"
test sourcePolicy = {
  ma|tch: {
    include: ['main.bicep']
  }
  assertions: {
    noExistingResources: {
      failOn: filter(target.resources, r => r.existing)
      message: 'Targets must create the resources they own.'
    }
  }
}
", '|');

        using var helper = await StartServerWithFiles(
            new Dictionary<DocumentUri, string>
            {
                [InMemoryFileResolver.GetFileUri("/path/to/main.bicep")] = TargetBicep,
                [testUri] = testText,
            },
            testUri);

        var file = new FileRequestHelper(helper.Client, new LanguageClientFile(testUri, testText));
        var hover = await file.RequestHover(cursor);

        // Hovering a property key resolves a property symbol, which only exists once the enclosing
        // object has a declared type. A test body had none, so these keys used to hover as nothing.
        hover.Should().NotBeNull();
        hover!.Contents.MarkupContent!.Value.Should().Contain("Selects the files this test runs against");
    }

    private async Task<PublishDiagnosticsParams> OpenTestFileForDiagnostics(string testFileText)
    {
        var testUri = InMemoryFileResolver.GetFileUri("/path/to/policy.biceptest");
        var mainUri = InMemoryFileResolver.GetFileUri("/path/to/main.bicep");

        var fileResolver = new InMemoryFileResolver(new Dictionary<Uri, string>
        {
            [mainUri] = TargetBicep,
        });

        using var helper = await MultiFileLanguageServerHelper.StartLanguageServer(
            TestContext,
            services => services
                .WithFileExplorer(new FileSystemFileExplorer(fileResolver.MockFileSystem))
                .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));

        return await helper.OpenFileOnceAsync(TestContext, testFileText, testUri);
    }

    private Task<LanguageServerHelper> StartServerWithFiles(IReadOnlyDictionary<DocumentUri, string> files, DocumentUri entryFileUri)
        => LanguageServerHelper.StartServerWithText(
            TestContext,
            files.ToImmutableDictionary(),
            entryFileUri,
            services => services.WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));
}
