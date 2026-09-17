// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Bicep.Core.TestFramework;

/// <summary>
/// A test-owned, statically declared description of which source files a test applies to.
/// Every value is a literal: the selector must be resolvable before any target is bound or evaluated.
/// </summary>
/// <param name="Root">
/// The directory the include and exclude patterns are relative to, itself relative to the directory
/// containing the test file. Traversal never leaves this directory.
/// </param>
/// <param name="Include">Patterns selecting files under the root. At least one is required.</param>
/// <param name="Exclude">Patterns removing files from the included set. Excludes always win over includes.</param>
/// <param name="AllowEmpty">
/// Whether selecting no files at all is acceptable. Defaults to false so that a selector which
/// silently stops matching anything is reported rather than passing vacuously.
/// </param>
public record TestTargetSelector(
    string Root,
    ImmutableArray<string> Include,
    ImmutableArray<string> Exclude,
    bool AllowEmpty)
{
    public const string RootPropertyName = "root";
    public const string IncludePropertyName = "include";
    public const string ExcludePropertyName = "exclude";
    public const string AllowEmptyPropertyName = "allowEmpty";

    public const string DefaultRoot = ".";

    /// <summary>
    /// The property names a selector may declare. Anything else is rejected.
    /// </summary>
    public static readonly ImmutableArray<string> KnownPropertyNames =
        [RootPropertyName, IncludePropertyName, ExcludePropertyName, AllowEmptyPropertyName];

    public static TestTargetSelector Create(
        string? root,
        IEnumerable<string> include,
        IEnumerable<string>? exclude = null,
        bool allowEmpty = false)
        => new(root ?? DefaultRoot, [.. include], [.. exclude ?? []], allowEmpty);

    /// <summary>
    /// Whether an include or exclude pattern would reach outside the selector root. Only the root may
    /// widen the search, so that reading a selector is enough to know the bounds of the walk.
    /// </summary>
    /// <remarks>
    /// The judgement is made from the pattern text alone rather than by asking the host whether the
    /// path is rooted. A test file is source that is commonly authored on one operating system and run
    /// on another, so a drive-qualified pattern is rejected everywhere instead of being read as an
    /// ordinary relative name wherever drive letters carry no meaning.
    /// </remarks>
    public static bool PatternEscapesRoot(string pattern)
    {
        var normalized = NormalizeSeparators(pattern);

        if (normalized.StartsWith('/') || HasDriveQualifier(normalized))
        {
            return true;
        }

        return normalized.Split('/').Any(segment => segment == "..");
    }

    private static bool HasDriveQualifier(string normalizedPattern)
        => normalizedPattern.Length >= 2 &&
           char.IsAsciiLetter(normalizedPattern[0]) &&
           normalizedPattern[1] == ':';

    public static string NormalizeSeparators(string pattern) => pattern.Replace('\\', '/');
}
