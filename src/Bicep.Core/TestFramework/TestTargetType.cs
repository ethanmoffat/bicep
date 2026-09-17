// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.TestFramework;

/// <summary>
/// The declared type of the compiler-provided <c>target</c> symbol.
///
/// Every object here is closed: it declares no additional properties, so an assertion that misspells a
/// fact name is reported as an unknown property instead of quietly evaluating to a null that would make
/// the policy pass.
/// </summary>
public static class TestTargetType
{
    public const string ResourcesPropertyName = "resources";
    public const string ModulesPropertyName = "modules";
    public const string ImportsPropertyName = "imports";
    public const string WithModulesPropertyName = "withModules";

    public const string NamePropertyName = "name";
    public const string SymbolicNamePropertyName = "symbolicName";
    public const string TypePropertyName = "type";
    public const string ExistingPropertyName = "existing";
    public const string PathPropertyName = "path";
    public const string ResolvedFilePropertyName = "resolvedFile";
    public const string SymbolsPropertyName = "symbols";
    public const string WildcardPropertyName = "wildcard";
    public const string FilePropertyName = "file";
    public const string LinePropertyName = "line";

    private static readonly ObjectType ResourceFact = new(
        "resourceFact",
        TypeSymbolValidationFlags.Default,
        [
            new NamedTypeProperty(SymbolicNamePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The symbolic name of the declaration."),
            new NamedTypeProperty(TypePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The resource type without its API version, for example 'Microsoft.Sql/servers'."),
            new NamedTypeProperty(ExistingPropertyName, LanguageConstants.Bool, TypePropertyFlags.ReadOnly, "Whether the declaration references an existing resource rather than declaring a new one."),
            new NamedTypeProperty(FilePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The declaring file, relative to the selector root and always using '/' separators."),
            new NamedTypeProperty(LinePropertyName, LanguageConstants.Int, TypePropertyFlags.ReadOnly, "The 1-based line the declaration starts on."),
        ],
        null);

    private static readonly ObjectType ModuleFact = new(
        "moduleFact",
        TypeSymbolValidationFlags.Default,
        [
            new NamedTypeProperty(SymbolicNamePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The symbolic name of the declaration."),
            new NamedTypeProperty(PathPropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The path exactly as spelled in source."),
            new NamedTypeProperty(ResolvedFilePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The file the path resolved to, relative to the selector root. Empty if it did not resolve to a local file."),
            new NamedTypeProperty(FilePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The declaring file, relative to the selector root and always using '/' separators."),
            new NamedTypeProperty(LinePropertyName, LanguageConstants.Int, TypePropertyFlags.ReadOnly, "The 1-based line the declaration starts on."),
        ],
        null);

    private static readonly ObjectType ImportFact = new(
        "importFact",
        TypeSymbolValidationFlags.Default,
        [
            new NamedTypeProperty(PathPropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The path exactly as spelled in source."),
            new NamedTypeProperty(ResolvedFilePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The file the path resolved to, relative to the selector root. Empty if it did not resolve to a local file."),
            new NamedTypeProperty(SymbolsPropertyName, new TypedArrayType(LanguageConstants.String, TypeSymbolValidationFlags.Default), TypePropertyFlags.ReadOnly, "The names the statement imports, in source order. Empty for a wildcard import."),
            new NamedTypeProperty(WildcardPropertyName, LanguageConstants.Bool, TypePropertyFlags.ReadOnly, "Whether the statement imports the whole namespace."),
            new NamedTypeProperty(FilePropertyName, LanguageConstants.String, TypePropertyFlags.ReadOnly, "The declaring file, relative to the selector root and always using '/' separators."),
            new NamedTypeProperty(LinePropertyName, LanguageConstants.Int, TypePropertyFlags.ReadOnly, "The 1-based line the statement starts on."),
        ],
        null);

    private static readonly ObjectType FactSet = CreateFactSet(WithModulesPropertyName);

    private static readonly ObjectType Target = new(
        LanguageConstants.TestTargetName,
        TypeSymbolValidationFlags.Default,
        [
            .. CreateFactProperties(),
            new NamedTypeProperty(
                WithModulesPropertyName,
                FactSet,
                TypePropertyFlags.ReadOnly,
                "The same facts for the selected file together with every local module reachable from it."),
        ],
        null);

    public static ObjectType Create() => Target;

    private static ObjectType CreateFactSet(string name) => new(
        name,
        TypeSymbolValidationFlags.Default,
        CreateFactProperties(),
        null);

    private static NamedTypeProperty[] CreateFactProperties() =>
    [
        new NamedTypeProperty(
            ResourcesPropertyName,
            new TypedArrayType(ResourceFact, TypeSymbolValidationFlags.Default),
            TypePropertyFlags.ReadOnly,
            "Resources declared in source, including nested ones and those inside conditions or loops."),
        new NamedTypeProperty(
            ModulesPropertyName,
            new TypedArrayType(ModuleFact, TypeSymbolValidationFlags.Default),
            TypePropertyFlags.ReadOnly,
            "Module declarations, preserving both the path as written and the file it resolved to."),
        new NamedTypeProperty(
            ImportsPropertyName,
            new TypedArrayType(ImportFact, TypeSymbolValidationFlags.Default),
            TypePropertyFlags.ReadOnly,
            "Compile-time import statements. One fact per statement, however many symbols it names."),
    ];
}
