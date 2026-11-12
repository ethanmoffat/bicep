// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core;
using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.IO.Abstraction;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// The cases one test parameters file contributes, or the reason it contributed none.
/// </summary>
public record TestInputFileResult(IOUri InputFile, ImmutableArray<TestInputCase> Cases, string? Error, IOUri? BoundTestFile = null)
{
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Reads the named input cases out of a compiled test parameters file.
///
/// A case body is ordinary Bicep, so it is evaluated the same way an assertion is: offline, with no
/// deployment and no provider calls.
/// </summary>
public static class TestInputCaseLoader
{
    public const string CompilationError = "The input file has compilation errors and cannot supply any cases.";

    public static TestInputFileResult Load(SemanticModel inputFileModel, IOUri expectedTestFile)
    {
        var inputFileUri = inputFileModel.SourceFile.FileHandle.Uri;
        var hasErrors = inputFileModel.HasErrors();

        // An input file binds to exactly one test file. Silently running its cases against a different
        // test would report results for inputs that were never written for it.
        if (!inputFileModel.Root.TryGetTestFileSemanticModelViaUsing().IsSuccess(out var boundTestModel, out var failure))
        {
            return new(inputFileUri, [], hasErrors ? CompilationError : failure.Message);
        }

        var boundUri = boundTestModel.SourceFile.FileHandle.Uri;

        if (boundUri != expectedTestFile)
        {
            // The bound file is reported alongside the message so a caller running several test
            // files can tell "these cases belong to one of the others" from "these cases belong to
            // a test file nobody is running".
            return new(inputFileUri, [], $"The input file supplies cases for \"{boundUri.GetPathRelativeTo(inputFileUri)}\", not the test file being run.", boundUri);
        }

        // From here on the file is known to be this test file's input, so whatever stops it is
        // reported as one of this test file's results rather than as a file that was passed over.
        // An input file that does not compile supplies no cases at all, as a template that does not
        // compile deploys nothing. Where the errors lie only decides how the result is named: an error
        // outside every case belongs to the file, while one inside a case is reported under that
        // case's name and its siblings say which case stopped them.
        var caseDeclarations = inputFileModel.Root.TestCaseDeclarations;

        if (hasErrors)
        {
            var errorPositions = inputFileModel.GetAllDiagnostics().Where(x => x.IsError()).Select(x => x.Span.Position).ToImmutableArray();

            if (errorPositions.Any(position => !caseDeclarations.Any(declaration => Contains(declaration.DeclaringTestCase, position))))
            {
                return new(inputFileUri, [TestInputCase.Failed(inputFileUri, null, CompilationError)], null);
            }

            var broken = caseDeclarations.Where(declaration => errorPositions.Any(position => Contains(declaration.DeclaringTestCase, position))).Select(x => x.Name).ToImmutableArray();
            var siblingError = $"The input file has compilation errors in {(broken.Length == 1 ? "case" : "cases")} {string.Join(", ", broken.Select(x => $"\"{x}\""))}, reported above, so none of its cases were evaluated.";

            return new(inputFileUri, [.. caseDeclarations.Select(declaration => TestInputCase.Failed(
                inputFileUri,
                declaration.Name,
                broken.Contains(declaration.Name)
                    ? $"The case \"{declaration.Name}\" has errors, reported above, so none of the file's cases were evaluated."
                    : siblingError))], null);
        }

        var context = new EmitterContext(inputFileModel);
        var cases = ImmutableArray.CreateBuilder<TestInputCase>();
        var fileContext = TestDeploymentContext.Empty;

        // File-level defaults are resolved once and never mutated, so one case's overrides can never
        // leak into another.
        foreach (var declaration in inputFileModel.SourceFile.ProgramSyntax.Children.OfType<DeploymentContextDeclarationSyntax>())
        {
            try
            {
                if (BicepValueEvaluator.Evaluate(context, declaration.Value, "object") is JObject declared)
                {
                    fileContext = fileContext.With(TestDeploymentContext.FromObject(declared));
                }
            }
            catch (Exception exception)
            {
                return new(inputFileUri, [TestInputCase.Failed(inputFileUri, null, $"The deployment context could not be evaluated: {BicepValueEvaluator.Sanitize(exception)}")], null);
            }
        }

        // Each case stands alone: one that cannot be evaluated is recorded under its own name and the
        // rest of the file still runs, because a case's values never depend on another case's.
        foreach (var declaration in caseDeclarations)
        {
            if (declaration.DeclaringTestCase.Body is not { } body)
            {
                cases.Add(TestInputCase.Failed(inputFileUri, declaration.Name, $"The case \"{declaration.Name}\" does not declare a body."));
                continue;
            }

            try
            {
                var evaluated = BicepValueEvaluator.Evaluate(context, body, "object");
                var values = evaluated is JObject o
                    ? o.Properties().ToImmutableDictionary(p => p.Name, p => p.Value, LanguageConstants.IdentifierComparer)
                    : [];

                cases.Add(new TestInputCase(inputFileUri, declaration.Name, values, ResolveCaseContext(context, declaration.DeclaringTestCase, fileContext)));
            }
            catch (Exception exception)
            {
                cases.Add(TestInputCase.Failed(inputFileUri, declaration.Name, $"The case \"{declaration.Name}\" could not be evaluated: {BicepValueEvaluator.Sanitize(exception)}"));
            }
        }

        return new(inputFileUri, cases.ToImmutable(), null);
    }

    /// <summary>
    /// Whether a diagnostic belongs to a case. The decorators in front of a case are part of it, since
    /// they set that case's deployment context.
    /// </summary>
    private static bool Contains(TestCaseDeclarationSyntax declaration, int position)
    {
        var start = declaration.Decorators.FirstOrDefault()?.Span.Position ?? declaration.Span.Position;

        return position >= start && position <= declaration.Span.Position + declaration.Span.Length;
    }

    /// <summary>
    /// Applies a case's property decorators over the file's defaults. Each decorator replaces exactly
    /// one property; anything it does not name is inherited unchanged.
    /// </summary>
    private static TestDeploymentContext ResolveCaseContext(EmitterContext context, TestCaseDeclarationSyntax declaration, TestDeploymentContext fileContext)
    {
        var resolved = fileContext;

        foreach (var decorator in declaration.Decorators)
        {
            if (decorator.Expression is not FunctionCallSyntax { Arguments.Length: 1 } call)
            {
                continue;
            }

            if (call.Name.IdentifierName == LanguageConstants.DeploymentContextEnvironmentPropertyName)
            {
                if (BicepValueEvaluator.Evaluate(context, call.Arguments[0].Expression, "object") is JObject environment)
                {
                    resolved = resolved.WithEnvironment(environment);
                }

                continue;
            }

            if (BicepValueEvaluator.Evaluate(context, call.Arguments[0].Expression, "string").Value<string>() is { } value)
            {
                resolved = resolved.WithProperty(call.Name.IdentifierName, value);
            }
        }

        return resolved;
    }
}
