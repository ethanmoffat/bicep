// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TestFramework;
using Bicep.IO.Abstraction;

namespace Bicep.Cli.Services;

/// <summary>
/// One discovered test case, or the reason a test could not be resolved to any.
/// </summary>
/// <param name="Identity">The case identity. For an unresolved test the target is the test file itself.</param>
/// <param name="Error">Why the test resolved to no targets, or null when the case was resolved.</param>
public record TestInventoryEntry(TestCaseIdentity Identity, string? Error)
{
    public bool IsResolved => Error is null;
}

/// <summary>
/// What a test file covers, without compiling or evaluating any target.
/// </summary>
/// <param name="Entries">Resolved cases and unresolved tests, in deterministic order.</param>
/// <param name="SkippedDirectories">
/// Directories that were deliberately not walked into. Reported so that a partial walk is never
/// mistaken for an exhaustive one.
/// </param>
public record TestInventory(ImmutableArray<TestInventoryEntry> Entries, ImmutableArray<IOUri> SkippedDirectories)
{
    public bool HasErrors => Entries.Any(entry => !entry.IsResolved);
}

/// <summary>
/// Resolves which targets a test file covers.
///
/// This deliberately performs no compilation, restore or evaluation of the targets: it answers
/// "what would run", and must never imply that those targets compile or pass.
/// </summary>
public static class TestDiscoveryService
{
    public static TestInventory Discover(SemanticModel testFileModel)
    {
        var testFileHandle = testFileModel.SourceFile.FileHandle;
        var testFileUri = testFileHandle.Uri;
        var entries = ImmutableArray.CreateBuilder<TestInventoryEntry>();
        var skippedDirectories = ImmutableArray.CreateBuilder<IOUri>();

        foreach (var testDeclaration in testFileModel.Root.TestDeclarations)
        {
            var declaration = testDeclaration.DeclaringTest;

            if (!declaration.IsTargetless)
            {
                entries.Add(DiscoverLiteralTarget(testFileUri, testDeclaration));
                continue;
            }

            if (TestTargetSelectorBinder.TryBind(declaration) is not { } selector)
            {
                entries.Add(Unresolved(testFileUri, testDeclaration.Name, "The test declares no usable 'match' selector."));
                continue;
            }

            var discovery = TestTargetDiscovery.Discover(testFileHandle.GetParent(), selector);

            skippedDirectories.AddRange(discovery.SkippedDirectories);

            if (discovery.Error is { } error)
            {
                entries.Add(Unresolved(testFileUri, testDeclaration.Name, error.Message));
                continue;
            }

            foreach (var targetUri in discovery.Targets)
            {
                entries.Add(new(new(testFileUri, testDeclaration.Name, targetUri), null));
            }
        }

        return new(entries.ToImmutable(), skippedDirectories.Distinct().ToImmutableArray());
    }

    private static TestInventoryEntry DiscoverLiteralTarget(IOUri testFileUri, TestSymbol testDeclaration)
    {
        // The literal path is resolved without restoring or compiling it. Whether the target exists
        // is a compilation concern, reported by the diagnostics the test file already produces.
        if (testDeclaration.DeclaringTest.TryGetPath()?.TryGetLiteralValue() is not { } path)
        {
            return Unresolved(testFileUri, testDeclaration.Name, "The test target path could not be read.");
        }

        return new(new(testFileUri, testDeclaration.Name, testFileUri.Resolve(path)), null);
    }

    private static TestInventoryEntry Unresolved(IOUri testFileUri, string testName, string error)
        => new(new(testFileUri, testName, testFileUri), error);
}
