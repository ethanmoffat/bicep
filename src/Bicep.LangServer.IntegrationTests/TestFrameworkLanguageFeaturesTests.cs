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

    private Task<LanguageServerHelper> StartServerWithFiles(IReadOnlyDictionary<DocumentUri, string> files, DocumentUri entryFileUri)
        => LanguageServerHelper.StartServerWithText(
            TestContext,
            files.ToImmutableDictionary(),
            entryFileUri,
            services => services.WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true)));
}
