// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Emit;
using Bicep.Core.Navigation;
using Bicep.Core.Parsing;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.TypeSystem;
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
        var parameters = ImmutableArray.CreateBuilder<TestParameterFact>();
        var outputs = ImmutableArray.CreateBuilder<TestOutputFact>();
        var visited = new HashSet<IOUri>();

        CollectTransitively(targetModel, factRoot, visited, resources, modules, imports, parameters, outputs);

        return new TestTargetFacts(
            local,
            new TestFactSet(resources.ToImmutable(), modules.ToImmutable(), imports.ToImmutable(), parameters.ToImmutable(), outputs.ToImmutable()),
            FormatTargetScope(targetModel.TargetScope));
    }

    /// <summary>
    /// Spells a scope the way the <c>targetScope</c> keyword does, so an assertion compares against the
    /// value an author would write.
    /// </summary>
    private static string FormatTargetScope(ResourceScope scope) => scope switch
    {
        ResourceScope.Tenant => LanguageConstants.TargetScopeTypeTenant,
        ResourceScope.ManagementGroup => LanguageConstants.TargetScopeTypeManagementGroup,
        ResourceScope.Subscription => LanguageConstants.TargetScopeTypeSubscription,
        ResourceScope.Local => LanguageConstants.TargetScopeTypeLocal,
        _ => LanguageConstants.TargetScopeTypeResourceGroup,
    };

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
        ImmutableArray<TestImportFact>.Builder imports,
        ImmutableArray<TestParameterFact>.Builder parameters,
        ImmutableArray<TestOutputFact>.Builder outputs)
    {
        if (!visited.Add(model.SourceFile.FileHandle.Uri))
        {
            return;
        }

        var facts = CollectFrom(model, factRoot);

        resources.AddRange(facts.Resources);
        modules.AddRange(facts.Modules);
        imports.AddRange(facts.Imports);
        parameters.AddRange(facts.Parameters);
        outputs.AddRange(facts.Outputs);

        foreach (var moduleSymbol in model.Root.ModuleDeclarations)
        {
            if (TryGetReferencedModel(moduleSymbol) is { } referenced)
            {
                CollectTransitively(referenced, factRoot, visited, resources, modules, imports, parameters, outputs);
            }
        }
    }

    private static TestFactSet CollectFrom(SemanticModel model, IOUri factRoot)
    {
        var file = RelativePath(model.SourceFile.FileHandle.Uri, factRoot);
        var lineStarts = model.SourceFile.LineStarts;
        var dependencies = ResourceDependencyVisitor.GetResourceDependencies(model);

        var resources = model.DeclaredResources
            .Select(resource => new TestResourceFact(
                resource.Symbol.Name,
                resource.Type.TypeReference.FormatType(),
                resource.IsExistingResource,
                file,
                GetLine(lineStarts, resource.Symbol.DeclaringSyntax),
                WaitsFor(resource.Symbol, dependencies)))
            .ToImmutableArray();

        var modules = model.Root.ModuleDeclarations
            .Select(module => new TestModuleFact(
                module.Name,
                (module.DeclaringModule.Path as StringSyntax)?.TryGetLiteralValue() ?? string.Empty,
                TryGetReferencedModel(module) is { } referenced ? RelativePath(referenced.SourceFile.FileHandle.Uri, factRoot) : string.Empty,
                file,
                GetLine(lineStarts, module.DeclaringModule),
                WaitsFor(module, dependencies)))
            .ToImmutableArray();

        var imports = model.SourceFile.ProgramSyntax.Children
            .OfType<CompileTimeImportDeclarationSyntax>()
            .Select(import => CollectImport(model, import, file, lineStarts, factRoot))
            .ToImmutableArray();

        // Required-ness is the compiler's own answer, the same one a parameters file is checked against,
        // rather than a second definition that could disagree with it.
        var parameters = model.Root.ParameterDeclarations
            .Select(parameter => new TestParameterFact(
                parameter.Name,
                DeclaredTypeText(parameter.DeclaringParameter.Type),
                model.Parameters.TryGetValue(parameter.Name, out var metadata) && metadata.IsRequired,
                SyntaxHelper.TryGetDefaultValue(parameter.DeclaringParameter) is not null,
                file,
                GetLine(lineStarts, parameter.DeclaringSyntax)))
            .ToImmutableArray();

        var outputs = model.Root.OutputDeclarations
            .Select(output => new TestOutputFact(
                output.Name,
                DeclaredTypeText(output.DeclaringOutput.Type),
                file,
                GetLine(lineStarts, output.DeclaringSyntax)))
            .ToImmutableArray();

        return new TestFactSet(resources, modules, imports, parameters, outputs);
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

    /// <summary>
    /// Everything a declaration waits for, directly or not, from the same dependency analysis the emitter
    /// uses for ARM's <c>dependsOn</c>: explicit <c>dependsOn</c> entries, references and parent/child
    /// relationships. Variables are followed rather than listed. An <c>existing</c> resource is followed
    /// but never listed, because nothing deploys it; what its name or scope needs is still waited for.
    /// Names are returned in source order.
    /// </summary>
    private static ImmutableArray<string> WaitsFor(
        DeclaredSymbol declaration,
        ImmutableDictionary<DeclaredSymbol, ImmutableHashSet<ResourceDependency>> dependencies)
    {
        var reached = new HashSet<DeclaredSymbol>();
        var pending = new Stack<DeclaredSymbol>([declaration]);

        while (pending.TryPop(out var current))
        {
            if (!dependencies.TryGetValue(current, out var direct))
            {
                continue;
            }

            foreach (var dependency in direct)
            {
                if (!ReferenceEquals(dependency.Resource, declaration) && reached.Add(dependency.Resource))
                {
                    pending.Push(dependency.Resource);
                }
            }
        }

        return [.. reached
            .Where(symbol => symbol switch
            {
                ModuleSymbol => true,
                ResourceSymbol resource => !resource.DeclaringResource.IsExistingResource(),
                _ => false,
            })
            .OrderBy(symbol => symbol.DeclaringSyntax.Span.Position)
            .Select(symbol => symbol.Name)];
    }

    /// <summary>
    /// The declared type as its author wrote it, so a named type keeps its name. The compiler's own
    /// type name is not used because it is inconsistent for this purpose: an array of a named type keeps
    /// the name while a direct reference is shown by its structure. Whitespace, line breaks and comments
    /// between tokens collapse to a single space, so layout never changes the fact.
    /// </summary>
    private static string DeclaredTypeText(SyntaxBase typeSyntax)
    {
        var collector = new TokenTextCollector();
        collector.Visit(typeSyntax);
        return collector.ToString();
    }

    private sealed class TokenTextCollector : CstVisitor
    {
        private readonly System.Text.StringBuilder text = new();
        private bool pendingSpace;

        public override void VisitToken(Token token)
        {
            if (token.Type is TokenType.NewLine)
            {
                pendingSpace = true;
                return;
            }

            if (token.LeadingTrivia.Length > 0)
            {
                pendingSpace = true;
            }

            if (pendingSpace && text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(token.Text);
            pendingSpace = token.TrailingTrivia.Length > 0;
        }

        public override string ToString() => text.ToString();
    }

    private static string RelativePath(IOUri uri, IOUri factRoot)
        => uri.GetPathRelativeTo(factRoot);

    private static int GetLine(ImmutableArray<int> lineStarts, IPositionable positionable)
        => TextCoordinateConverter.GetPosition(lineStarts, positionable.Span.Position).line + 1;
}
