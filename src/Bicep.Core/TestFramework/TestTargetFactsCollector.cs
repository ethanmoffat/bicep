// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Navigation;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.IO.Abstraction;

namespace Bicep.Core.TestFramework;

/// <summary>
/// Reads the compiler's view of a target file and its locally reachable modules into the plain data
/// that a test assertion can query.
///
/// Everything collected here is a source fact. Conditions are not evaluated, loops are not expanded and
/// no deployment values are required, so a source policy can run without any deployment inputs at all.
/// </summary>
public static class TestTargetFactsCollector
{
    /// <param name="targetModel">The semantic model of the selected target.</param>
    /// <param name="factRoot">
    /// The directory that reported file paths are relative to. Making this the selector root rather than
    /// the working directory keeps a policy such as "declare SQL servers only under src/modules/sql/"
    /// meaningful no matter where the CLI was invoked from.
    /// </param>
    public static TestTargetFacts Collect(SemanticModel targetModel, IOUri factRoot)
    {
        var local = CollectFrom(targetModel, factRoot);

        var resources = ImmutableArray.CreateBuilder<TestResourceFact>();
        var modules = ImmutableArray.CreateBuilder<TestModuleFact>();
        var imports = ImmutableArray.CreateBuilder<TestImportFact>();
        var visited = new HashSet<IOUri>();

        CollectTransitively(targetModel, factRoot, visited, resources, modules, imports);

        return new TestTargetFacts(
            local,
            new TestFactSet(resources.ToImmutable(), modules.ToImmutable(), imports.ToImmutable()));
    }

    /// <summary>
    /// Visits each reachable file once. A file reached through two different callers contributes its
    /// declarations a single time, while each caller keeps its own distinct module reference site.
    /// </summary>
    private static void CollectTransitively(
        SemanticModel model,
        IOUri factRoot,
        HashSet<IOUri> visited,
        ImmutableArray<TestResourceFact>.Builder resources,
        ImmutableArray<TestModuleFact>.Builder modules,
        ImmutableArray<TestImportFact>.Builder imports)
    {
        if (!visited.Add(model.SourceFile.FileHandle.Uri))
        {
            return;
        }

        var facts = CollectFrom(model, factRoot);

        resources.AddRange(facts.Resources);
        modules.AddRange(facts.Modules);
        imports.AddRange(facts.Imports);

        foreach (var moduleSymbol in model.Root.ModuleDeclarations)
        {
            if (TryGetReferencedModel(moduleSymbol) is { } referenced)
            {
                CollectTransitively(referenced, factRoot, visited, resources, modules, imports);
            }
        }
    }

    private static TestFactSet CollectFrom(SemanticModel model, IOUri factRoot)
    {
        var file = RelativePath(model.SourceFile.FileHandle.Uri, factRoot);
        var lineStarts = model.SourceFile.LineStarts;

        var resources = model.DeclaredResources
            .Select(resource => new TestResourceFact(
                resource.Symbol.Name,
                resource.Type.TypeReference.FormatType(),
                resource.IsExistingResource,
                file,
                GetLine(lineStarts, resource.Symbol.DeclaringSyntax)))
            .ToImmutableArray();

        var modules = model.Root.ModuleDeclarations
            .Select(module => new TestModuleFact(
                module.Name,
                (module.DeclaringModule.Path as StringSyntax)?.TryGetLiteralValue() ?? string.Empty,
                TryGetReferencedModel(module) is { } referenced ? RelativePath(referenced.SourceFile.FileHandle.Uri, factRoot) : string.Empty,
                file,
                GetLine(lineStarts, module.DeclaringModule)))
            .ToImmutableArray();

        var imports = model.SourceFile.ProgramSyntax.Children
            .OfType<CompileTimeImportDeclarationSyntax>()
            .Select(import => CollectImport(model, import, file, lineStarts, factRoot))
            .ToImmutableArray();

        return new TestFactSet(resources, modules, imports);
    }

    private static TestImportFact CollectImport(
        SemanticModel model,
        CompileTimeImportDeclarationSyntax import,
        string file,
        ImmutableArray<int> lineStarts,
        IOUri factRoot)
    {
        var symbols = import.ImportExpression is ImportedSymbolsListSyntax list
            ? list.ImportedSymbols.Select(x => x.Name.IdentifierName).ToImmutableArray()
            : [];

        var resolvedFile = model.SourceFileGrouping.TryGetSourceFile(import).IsSuccess(out var sourceFile)
            ? RelativePath(sourceFile.FileHandle.Uri, factRoot)
            : string.Empty;

        return new TestImportFact(
            ((IArtifactReferenceSyntax)import).Path is StringSyntax path ? path.TryGetLiteralValue() ?? string.Empty : string.Empty,
            resolvedFile,
            symbols,
            import.ImportExpression is WildcardImportSyntax,
            file,
            GetLine(lineStarts, import));
    }

    private static SemanticModel? TryGetReferencedModel(ModuleSymbol module)
        => module.TryGetSemanticModel().IsSuccess(out var model) ? model as SemanticModel : null;

    private static string RelativePath(IOUri uri, IOUri factRoot)
        => uri.GetPathRelativeTo(factRoot);

    private static int GetLine(ImmutableArray<int> lineStarts, IPositionable positionable)
        => TextCoordinateConverter.GetPosition(lineStarts, positionable.Span.Position).line + 1;
}
