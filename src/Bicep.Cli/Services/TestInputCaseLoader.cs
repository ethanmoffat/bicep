// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core;
using Bicep.Core.Emit;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.IO.Abstraction;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// The cases one test parameters file contributes, or the reason it contributed none.
/// </summary>
public record TestInputFileResult(IOUri InputFile, ImmutableArray<TestInputCase> Cases, string? Error)
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
    public static TestInputFileResult Load(SemanticModel inputFileModel, IOUri expectedTestFile)
    {
        var inputFileUri = inputFileModel.SourceFile.FileHandle.Uri;

        if (inputFileModel.HasErrors())
        {
            return new(inputFileUri, [], "The input file has compilation errors and cannot supply any cases.");
        }

        // An input file binds to exactly one test file. Silently running its cases against a different
        // test would report results for inputs that were never written for it.
        if (!inputFileModel.Root.TryGetTestFileSemanticModelViaUsing().IsSuccess(out var boundTestModel, out var failure))
        {
            return new(inputFileUri, [], failure.Message);
        }

        var boundUri = boundTestModel.SourceFile.FileHandle.Uri;

        if (boundUri != expectedTestFile)
        {
            return new(inputFileUri, [], $"The input file supplies cases for \"{boundUri.GetPathRelativeTo(inputFileUri)}\", not the test file being run.");
        }

        var context = new EmitterContext(inputFileModel);
        var cases = ImmutableArray.CreateBuilder<TestInputCase>();

        foreach (var declaration in inputFileModel.Root.TestCaseDeclarations)
        {
            if (declaration.DeclaringTestCase.Body is not { } body)
            {
                return new(inputFileUri, [], $"The case \"{declaration.Name}\" does not declare a body.");
            }

            try
            {
                var evaluated = BicepValueEvaluator.Evaluate(context, body, "object");
                var values = evaluated is JObject o
                    ? o.Properties().ToImmutableDictionary(p => p.Name, p => p.Value, LanguageConstants.IdentifierComparer)
                    : [];

                cases.Add(new TestInputCase(inputFileUri, declaration.Name, values));
            }
            catch (Exception exception)
            {
                return new(inputFileUri, [], $"The case \"{declaration.Name}\" could not be evaluated: {BicepValueEvaluator.Sanitize(exception)}");
            }
        }

        return new(inputFileUri, cases.ToImmutable(), null);
    }
}
