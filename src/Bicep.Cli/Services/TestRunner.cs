// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Azure.Deployments.Core.Definitions.Schema;
using Bicep.Core;
using Bicep.Core.Emit;
using Bicep.Core.Intermediate;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TestFramework;
using Bicep.Core.Utils;
using Bicep.IO.Abstraction;
using Microsoft.WindowsAzure.ResourceStack.Common.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services
{
    public record TestResult(TestSymbol Source, TestCaseIdentity Identity, TestEvaluation Result);

    public record TestResults(ImmutableArray<TestResult> Results)
    {
        public int TotalEvaluations => Results.Length;

        public int SuccessfulEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Passed);

        public int FailedEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Failed);

        public int SkippedEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Skipped);

        public bool Success => FailedEvaluations == 0 && SkippedEvaluations == 0;
    }
    public class TestRunner(BicepCompiler compiler)
    {
        public async Task<TestResults> RunAsync(SemanticModel testFileModel)
        {
            var testFileUri = testFileModel.SourceFile.FileHandle.Uri;
            var testResults = ImmutableArray.CreateBuilder<TestResult>();

            foreach (var testDeclaration in testFileModel.Root.TestDeclarations)
            {
                if (testDeclaration.DeclaringTest.IsTargetless)
                {
                    testResults.AddRange(await RunSelectedTargetsAsync(testFileModel, testDeclaration));
                }
                else if (testDeclaration.TryGetSemanticModel().IsSuccess(out var semanticModel, out var _) &&
                    semanticModel is SemanticModel testSemanticModel)
                {
                    testResults.Add(Evaluate(testFileUri, testDeclaration, testSemanticModel));
                }
            }

            return new TestResults(testResults.ToImmutable());
        }

        /// <summary>
        /// Expands a body-owned selector into its targets and evaluates each one independently, so that
        /// a target which fails to compile or bind never hides the outcome of the others.
        /// </summary>
        private async Task<IEnumerable<TestResult>> RunSelectedTargetsAsync(SemanticModel testFileModel, TestSymbol testDeclaration)
        {
            var testFileHandle = testFileModel.SourceFile.FileHandle;
            var testFileUri = testFileHandle.Uri;

            if (TestTargetSelectorBinder.TryBind(testDeclaration.DeclaringTest) is not { } selector)
            {
                return [Unevaluated(testFileUri, testDeclaration, testFileUri, "The test declares no usable 'match' selector.")];
            }

            var discovery = TestTargetDiscovery.Discover(testFileHandle.GetParent(), selector);

            if (discovery.Error is { } error)
            {
                return [Unevaluated(testFileUri, testDeclaration, testFileUri, error.Message)];
            }

            var results = new List<TestResult>();

            foreach (var targetUri in discovery.Targets)
            {
                results.Add(await EvaluateTargetAsync(testFileUri, testDeclaration, targetUri));
            }

            return results;
        }

        private async Task<TestResult> EvaluateTargetAsync(IOUri testFileUri, TestSymbol testDeclaration, IOUri targetUri)
        {
            SemanticModel targetModel;

            try
            {
                var targetCompilation = await compiler.CreateCompilation(targetUri, skipRestore: true);
                targetModel = targetCompilation.GetEntrypointSemanticModel();
            }
            catch (Exception exception)
            {
                return Unevaluated(testFileUri, testDeclaration, targetUri, SanitizeEvaluationError(exception));
            }

            if (targetModel.HasErrors())
            {
                return Unevaluated(testFileUri, testDeclaration, targetUri, $"The target has compilation errors and cannot be evaluated.");
            }

            return Evaluate(testFileUri, testDeclaration, targetModel);
        }

        private static TestResult Evaluate(IOUri testFileUri, TestSymbol testDeclaration, SemanticModel targetModel)
        {
            var identity = new TestCaseIdentity(testFileUri, testDeclaration.Name, targetModel.SourceFile.FileHandle.Uri);
            TestEvaluation evaluation;

            try
            {
                var parameters = TryGetParameters(targetModel, testDeclaration);
                var templateJToken = GetTemplate(targetModel);
                var template = TemplateEvaluator.Evaluate(templateJToken, parameters);
                var allAssertions = template.Asserts?.Select(p => new AssertionResult(p.Key, (bool)p.Value.Value)).ToImmutableArray() ?? [];
                var failedAssertions = allAssertions.Where(a => !a.Result).Select(a => a).ToImmutableArray();

                evaluation = new TestEvaluation(template, null, allAssertions, failedAssertions);
            }
            catch (Exception exception)
            {
                evaluation = new TestEvaluation(null, SanitizeEvaluationError(exception), [], []);
            }

            return new TestResult(testDeclaration, identity, evaluation);
        }

        /// <summary>
        /// Reduces an evaluation exception to its first line. The ARM evaluator appends the full template
        /// and parameters payload to its messages, and a test result must never become a transcript of the
        /// parameter values it was given.
        /// </summary>
        private static string SanitizeEvaluationError(Exception exception)
        {
            var message = exception.Message;
            var lineBreak = message.IndexOfAny(['\r', '\n']);

            return lineBreak < 0 ? message : message[..lineBreak].TrimEnd();
        }

        private static TestResult Unevaluated(IOUri testFileUri, TestSymbol testDeclaration, IOUri targetUri, string error)
            => new(testDeclaration, new TestCaseIdentity(testFileUri, testDeclaration.Name, targetUri), new TestEvaluation(null, error, [], []));

        private static JToken GetTemplate(SemanticModel model)
        {
            var textWriter = new StringWriter();
            using var writer = new SourceAwareJsonTextWriter(textWriter)
            {
                // don't close the textWriter when writer is disposed
                CloseOutput = false,
                Formatting = Formatting.Indented
            };
            var (_, template) = new TemplateWriter(model).GetTemplate(writer);

            return template;
        }

        private static JObject? TryGetParameters(SemanticModel model, TestSymbol test)
        {
            if (test.DeclaringTest.GetBody() is { } body &&
                body.TryGetPropertyByName("params") is { } paramsProperty)
            {
                var textWriter = new StringWriter();
                using var writer = new PositionTrackingJsonTextWriter(textWriter)
                {
                    // don't close the textWriter when writer is disposed
                    CloseOutput = false,
                    Formatting = Formatting.Indented
                };

                var emitter = new ExpressionEmitter(writer, new(model));
                var parametersExpression = new ExpressionBuilder(new(model)).Convert(paramsProperty.Value);
                new TemplateWriter(model).EmitTestParameters(emitter, parametersExpression);
                writer.Flush();

                var parameters = textWriter.ToString().FromJson<JObject>().Properties()
                    .ToDictionary(x => x.Name, x => new JObject()
                    {
                        ["value"] = x.Value,
                    })
                    .ToJToken();

                return new JObject()
                {
                    ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
                    ["contentVersion"] = "1.0.0.0",
                    ["parameters"] = parameters,
                };
            }

            return null;
        }
    }
}
