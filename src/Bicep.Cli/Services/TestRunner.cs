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
        /// <summary>
        /// Runs every test in the file. When input cases are supplied, each test runs once per target per
        /// case: cases supply values, and can never change which targets a test applies to.
        /// </summary>
        public async Task<TestResults> RunAsync(SemanticModel testFileModel, ImmutableArray<TestInputCase> inputCases = default)
        {
            var testFileUri = testFileModel.SourceFile.FileHandle.Uri;
            var testResults = ImmutableArray.CreateBuilder<TestResult>();

            // With no input file a test runs once against whatever defaults it declares, which is what
            // every existing test file already relies on.
            TestInputCase?[] cases = inputCases.IsDefaultOrEmpty ? [null] : [.. inputCases.Select(x => (TestInputCase?)x)];

            foreach (var testDeclaration in testFileModel.Root.TestDeclarations)
            {
                foreach (var inputCase in cases)
                {
                    if (testDeclaration.DeclaringTest.IsTargetless)
                    {
                        testResults.AddRange(await RunSelectedTargetsAsync(testFileModel, testDeclaration, inputCase));
                    }
                    else if (testDeclaration.TryGetSemanticModel().IsSuccess(out var semanticModel, out var _) &&
                        semanticModel is SemanticModel testSemanticModel)
                    {
                        // A literal target names one file, so the test file's own directory is the frame of
                        // reference its facts are reported in.
                        testResults.Add(Evaluate(testFileModel, testDeclaration, testSemanticModel, testFileModel.SourceFile.FileHandle.GetParent().Uri, inputCase));
                    }
                }
            }

            return new TestResults(testResults.ToImmutable());
        }

        /// <summary>
        /// Expands a body-owned selector into its targets and evaluates each one independently, so that
        /// a target which fails to compile or bind never hides the outcome of the others.
        /// </summary>
        private async Task<IEnumerable<TestResult>> RunSelectedTargetsAsync(SemanticModel testFileModel, TestSymbol testDeclaration, TestInputCase? inputCase)
        {
            var testFileHandle = testFileModel.SourceFile.FileHandle;
            var testFileUri = testFileHandle.Uri;

            if (TestTargetSelectorBinder.TryBind(testDeclaration.DeclaringTest) is not { } selector)
            {
                return [Unevaluated(testFileUri, testDeclaration, testFileUri, "The test declares no usable 'match' selector.", inputCase)];
            }

            var discovery = TestTargetDiscovery.Discover(testFileHandle.GetParent(), selector);

            if (discovery.Error is { } error)
            {
                return [Unevaluated(testFileUri, testDeclaration, testFileUri, error.Message, inputCase)];
            }

            var results = new List<TestResult>();

            foreach (var targetUri in discovery.Targets)
            {
                results.Add(await EvaluateTargetAsync(testFileModel, testDeclaration, targetUri, discovery.Root ?? testFileHandle.GetParent().Uri, inputCase));
            }

            return results;
        }

        private async Task<TestResult> EvaluateTargetAsync(SemanticModel testFileModel, TestSymbol testDeclaration, IOUri targetUri, IOUri factRoot, TestInputCase? inputCase)
        {
            var testFileUri = testFileModel.SourceFile.FileHandle.Uri;
            SemanticModel targetModel;

            try
            {
                var targetCompilation = await compiler.CreateCompilation(targetUri, skipRestore: true);
                targetModel = targetCompilation.GetEntrypointSemanticModel();
            }
            catch (Exception exception)
            {
                return Unevaluated(testFileUri, testDeclaration, targetUri, SanitizeEvaluationError(exception), inputCase);
            }

            if (targetModel.HasErrors())
            {
                return Unevaluated(testFileUri, testDeclaration, targetUri, $"The target has compilation errors and cannot be evaluated.");
            }

            return Evaluate(testFileModel, testDeclaration, targetModel, factRoot, inputCase);
        }

        /// <summary>
        /// Runs the test's own assertions when it declares any, and the target template's assertions
        /// otherwise. The two sets are never run together: which assertions a case ran is part of what
        /// its result means.
        /// </summary>
        private static TestResult Evaluate(SemanticModel testFileModel, TestSymbol testDeclaration, SemanticModel targetModel, IOUri factRoot, TestInputCase? inputCase)
        {
            var testFileUri = testFileModel.SourceFile.FileHandle.Uri;
            var identity = new TestCaseIdentity(testFileUri, testDeclaration.Name, targetModel.SourceFile.FileHandle.Uri, inputCase);

            if (testDeclaration.DeclaringTest.TryGetAssertionsSyntax() is { Properties: { } declared } && declared.Any())
            {
                return new TestResult(testDeclaration, identity, EvaluateSemanticAssertions(testFileModel, testDeclaration, targetModel, factRoot, inputCase));
            }

            return new TestResult(testDeclaration, identity, EvaluateTargetTemplate(testFileModel, targetModel, testDeclaration, inputCase));
        }

        private static TestEvaluation EvaluateSemanticAssertions(SemanticModel testFileModel, TestSymbol testDeclaration, SemanticModel targetModel, IOUri factRoot, TestInputCase? inputCase)
        {
            try
            {
                var facts = TestTargetFactsCollector.Collect(targetModel, factRoot);
                var allAssertions = TestAssertionEvaluator.Evaluate(testFileModel, testDeclaration.DeclaringTest, facts, inputCase);
                var failedAssertions = allAssertions.Where(x => !x.Result).ToImmutableArray();

                return new TestEvaluation(null, null, allAssertions, failedAssertions);
            }
            catch (Exception exception)
            {
                return new TestEvaluation(null, SanitizeEvaluationError(exception), [], []);
            }
        }

        private static TestEvaluation EvaluateTargetTemplate(SemanticModel testFileModel, SemanticModel targetModel, TestSymbol testDeclaration, TestInputCase? inputCase)
        {
            try
            {
                var parameters = TryGetParameters(testFileModel, testDeclaration, inputCase);
                var templateJToken = GetTemplate(targetModel);
                var template = TemplateEvaluator.Evaluate(templateJToken, parameters, configBuilder: (inputCase?.Context ?? TestDeploymentContext.Empty).Apply);
                var allAssertions = template.Asserts?.Select(p => new AssertionResult(p.Key, (bool)p.Value.Value)).ToImmutableArray() ?? [];
                var failedAssertions = allAssertions.Where(a => !a.Result).Select(a => a).ToImmutableArray();

                return new TestEvaluation(template, null, allAssertions, failedAssertions);
            }
            catch (Exception exception)
            {
                return new TestEvaluation(null, SanitizeEvaluationError(exception), [], []);
            }
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

        private static TestResult Unevaluated(IOUri testFileUri, TestSymbol testDeclaration, IOUri targetUri, string error, TestInputCase? inputCase = null)
            => new(testDeclaration, new TestCaseIdentity(testFileUri, testDeclaration.Name, targetUri, inputCase), new TestEvaluation(null, error, [], []));

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

        /// <summary>
        /// Resolves the production parameters the test maps in. The mapping is ordinary Bicep evaluated
        /// against the test file, so a case's values reach the target through the test's own typed inputs
        /// rather than being injected into the target directly.
        /// </summary>
        private static JObject? TryGetParameters(SemanticModel testFileModel, TestSymbol test, TestInputCase? inputCase)
        {
            if (test.DeclaringTest.GetBody() is { } body &&
                body.TryGetPropertyByName("params") is { } paramsProperty)
            {
                var evaluated = BicepValueEvaluator.Evaluate(new EmitterContext(testFileModel), paramsProperty.Value, "object", inputValues: inputCase?.Values, deploymentContext: inputCase?.Context);

                if (evaluated is not JObject paramsObject)
                {
                    return null;
                }

                var parameters = paramsObject.Properties()
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
