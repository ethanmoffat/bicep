// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Bicep.Core.TestFramework;

/// <summary>
/// A resource declared in source. These are source facts: a resource declared inside a false condition
/// or a loop is still one declaration, and no attempt is made to describe the instances it would deploy.
/// </summary>
/// <param name="Name">The symbolic name of the declaration.</param>
/// <param name="Type">The resource type without its API version, for example 'Microsoft.Sql/servers'.</param>
/// <param name="Existing">Whether the declaration is an 'existing' reference rather than a new resource.</param>
/// <param name="File">The declaring file, relative to the fact root and always using '/' separators.</param>
/// <param name="Line">The 1-based line the declaration starts on.</param>
public record TestResourceFact(string Name, string Type, bool Existing, string File, int Line);

/// <summary>
/// A module declaration, preserving both the path as written and the file it resolved to.
/// </summary>
/// <param name="Name">The symbolic name of the declaration.</param>
/// <param name="Path">The path exactly as spelled in source.</param>
/// <param name="ResolvedFile">The resolved file relative to the fact root, or an empty string if it did not resolve to a local file.</param>
/// <param name="File">The declaring file, relative to the fact root and always using '/' separators.</param>
/// <param name="Line">The 1-based line the declaration starts on.</param>
public record TestModuleFact(string Name, string Path, string ResolvedFile, string File, int Line);

/// <summary>
/// A compile-time import statement. One fact describes one statement, however many symbols it names,
/// so that a policy counting prohibited references never multiplies a single reference site.
/// </summary>
/// <param name="Path">The path exactly as spelled in source.</param>
/// <param name="ResolvedFile">The resolved file relative to the fact root, or an empty string if it did not resolve to a local file.</param>
/// <param name="Symbols">The names imported by the statement, in source order. Empty for a wildcard import.</param>
/// <param name="Wildcard">Whether the statement imports the whole namespace.</param>
/// <param name="File">The declaring file, relative to the fact root and always using '/' separators.</param>
/// <param name="Line">The 1-based line the statement starts on.</param>
public record TestImportFact(string Path, string ResolvedFile, ImmutableArray<string> Symbols, bool Wildcard, string File, int Line);

/// <summary>
/// The declarations visible at one query scope.
/// </summary>
public record TestFactSet(
    ImmutableArray<TestResourceFact> Resources,
    ImmutableArray<TestModuleFact> Modules,
    ImmutableArray<TestImportFact> Imports)
{
    public static readonly TestFactSet Empty = new([], [], []);
}

/// <summary>
/// The compiler facts a test can assert over for one target.
/// <see cref="Local"/> describes the selected file alone; <see cref="WithModules"/> additionally
/// describes every local module reachable from it, so a file-scoped policy can never accidentally
/// attribute a child's declarations to its caller.
/// </summary>
public record TestTargetFacts(TestFactSet Local, TestFactSet WithModules)
{
    public static readonly TestTargetFacts Empty = new(TestFactSet.Empty, TestFactSet.Empty);
}
