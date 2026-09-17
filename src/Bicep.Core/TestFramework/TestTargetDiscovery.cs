// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.IO.Abstraction;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Bicep.Core.TestFramework;

public enum TestTargetDiscoveryErrorKind
{
    /// <summary>The selector declared no include patterns.</summary>
    NoIncludePatterns,

    /// <summary>An include or exclude pattern tried to leave the selector root.</summary>
    PatternEscapesRoot,

    /// <summary>The selector root does not exist.</summary>
    RootNotFound,

    /// <summary>Walking the tree under the root failed.</summary>
    EnumerationFailed,

    /// <summary>The selector matched no files and did not opt in to being empty.</summary>
    NoTargetsMatched,
}

public record TestTargetDiscoveryError(TestTargetDiscoveryErrorKind Kind, string Message);

/// <summary>
/// The outcome of expanding one selector.
/// </summary>
/// <param name="Targets">The matched files, deduplicated and deterministically ordered.</param>
/// <param name="SkippedDirectories">
/// Directories that were deliberately not walked into, currently symbolic links and junctions.
/// These are reported rather than silently treated as empty, so that a partial walk is never
/// mistaken for an exhaustive one.
/// </param>
/// <param name="Error">The reason discovery failed, or null when it succeeded.</param>
public record TestTargetDiscoveryResult(
    ImmutableArray<IOUri> Targets,
    ImmutableArray<IOUri> SkippedDirectories,
    TestTargetDiscoveryError? Error)
{
    public bool IsSuccess => Error is null;

    /// <summary>
    /// The directory the selector resolved to. Reported so that everything downstream can describe a
    /// target in the same selector-relative terms the author used to select it.
    /// </summary>
    public IOUri? Root { get; init; }
}

/// <summary>
/// Expands a <see cref="TestTargetSelector"/> into the concrete set of files a test applies to.
///
/// Discovery is deliberately a pure filesystem concern: it answers "which files", never "which files
/// are interesting", so that a semantic assertion can never silently shrink the inventory.
/// </summary>
public static class TestTargetDiscovery
{
    public static TestTargetDiscoveryResult Discover(IDirectoryHandle testFileDirectory, TestTargetSelector selector)
    {
        if (selector.Include.IsDefaultOrEmpty)
        {
            return Failure(TestTargetDiscoveryErrorKind.NoIncludePatterns, "The selector must declare at least one include pattern.");
        }

        foreach (var pattern in selector.Include.Concat(selector.Exclude))
        {
            if (TestTargetSelector.PatternEscapesRoot(pattern))
            {
                return Failure(
                    TestTargetDiscoveryErrorKind.PatternEscapesRoot,
                    $"The pattern \"{pattern}\" must not be rooted or contain \"..\" segments. Use the selector root to select a different directory.");
            }
        }

        var rootDirectory = selector.Root is TestTargetSelector.DefaultRoot or ""
            ? testFileDirectory
            : testFileDirectory.GetDirectory(NormalizeSeparators(selector.Root));

        if (!rootDirectory.Exists())
        {
            return Failure(TestTargetDiscoveryErrorKind.RootNotFound, $"The selector root \"{selector.Root}\" does not exist.");
        }

        ImmutableArray<IOUri> skippedDirectories;
        Dictionary<string, IOUri> filesByRelativePath;

        try
        {
            (filesByRelativePath, skippedDirectories) = EnumerateFilesUnderRoot(rootDirectory);
        }
        catch (Exception exception)
        {
            return Failure(TestTargetDiscoveryErrorKind.EnumerationFailed, $"Failed to enumerate files under \"{selector.Root}\": {exception.Message}");
        }

        var matcher = new Matcher(IOUri.GlobalSettings.LocalFilePathComparison);

        foreach (var include in selector.Include)
        {
            matcher.AddInclude(NormalizeSeparators(include));
        }

        // Excludes are added after includes, but Matcher applies them as a filter over the
        // included set regardless of ordering, so an excluded file can never be reinstated.
        foreach (var exclude in selector.Exclude)
        {
            matcher.AddExclude(NormalizeSeparators(exclude));
        }

        var matched = matcher.Match(filesByRelativePath.Keys);

        // A file matched by several include patterns is still one target, so dedupe by URI.
        var targets = matched.Files
            .Select(match => filesByRelativePath[match.Path])
            .Distinct()
            .OrderBy(uri => uri.ToString(), IOUri.GlobalSettings.LocalFilePathComparer)
            .ToImmutableArray();

        if (targets.IsEmpty && !selector.AllowEmpty)
        {
            return new(
                [],
                skippedDirectories,
                new(
                    TestTargetDiscoveryErrorKind.NoTargetsMatched,
                    $"The selector matched no files under \"{selector.Root}\". Set \"{TestTargetSelector.AllowEmptyPropertyName}\" to true if this is expected."))
            { Root = rootDirectory.Uri };
        }

        return new(targets, skippedDirectories, null) { Root = rootDirectory.Uri };
    }

    private static (Dictionary<string, IOUri> filesByRelativePath, ImmutableArray<IOUri> skippedDirectories) EnumerateFilesUnderRoot(IDirectoryHandle root)
    {
        var filesByRelativePath = new Dictionary<string, IOUri>(IOUri.GlobalSettings.LocalFilePathComparer);
        var skippedDirectories = ImmutableArray.CreateBuilder<IOUri>();
        var visitedDirectories = new HashSet<string>(IOUri.GlobalSettings.LocalFilePathComparer);
        var pending = new Stack<(IDirectoryHandle directory, string relativePath)>();

        pending.Push((root, ""));

        while (pending.Count > 0)
        {
            var (directory, relativePath) = pending.Pop();

            if (!visitedDirectories.Add(directory.Uri.ToString()))
            {
                continue;
            }

            foreach (var file in directory.EnumerateFiles())
            {
                var fileName = file.Uri.PathSegments[^1];
                filesByRelativePath[Combine(relativePath, fileName)] = file.Uri;
            }

            foreach (var child in directory.EnumerateDirectories())
            {
                // Links are not followed: the selector root is meant to bound the walk, and a link
                // can point anywhere, including back into an ancestor.
                if (child.IsSymbolicLink())
                {
                    skippedDirectories.Add(child.Uri);
                    continue;
                }

                var childName = child.Uri.PathSegments[^1];
                pending.Push((child, Combine(relativePath, childName)));
            }
        }

        return (filesByRelativePath, skippedDirectories.ToImmutable());
    }

    private static string Combine(string relativeDirectory, string name)
        => relativeDirectory.Length == 0 ? name : $"{relativeDirectory}/{name}";

    private static string NormalizeSeparators(string pattern) => TestTargetSelector.NormalizeSeparators(pattern);

    private static TestTargetDiscoveryResult Failure(TestTargetDiscoveryErrorKind kind, string message)
        => new([], [], new(kind, message));
}
