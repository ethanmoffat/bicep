// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
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
    /// <summary>
    /// One evaluation and how long it took. <see cref="Duration"/> covers the whole unit of work a
    /// host would attribute to this case, including compiling the target, because that is the cost a
    /// slow policy actually imposes. It is set once where the result is collected rather than by each
    /// site that constructs one, so no path can report an unmeasured zero.
    /// </summary>
    public record TestResult(TestSymbol Source, TestCaseIdentity Identity, TestEvaluation Result)
    {
        public TimeSpan Duration { get; init; }
    }

    public record TestResults(ImmutableArray<TestResult> Results)
    {
        public int TotalEvaluations => Results.Length;

        /// <summary>
        /// The summed cost of the evaluations, not wall-clock time for the run. The two coincide while
        /// evaluation is sequential, and summing stays correct if it ever stops being.
        /// </summary>
        public TimeSpan TotalDuration => Results.Aggregate(TimeSpan.Zero, (total, result) => total + result.Duration);

        public int SuccessfulEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Passed);

        public int FailedEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Failed);

        /// <summary>
        /// Evaluations that could not run at all. Counted separately from failures because the two say
        /// different things - a failure means the policy was broken, an error means it never ran - but
        /// either one makes the run a failure.
        /// </summary>
        public int ErroredEvaluations => Results.Count(x => x.Result.Status == TestCaseStatus.Errored);

        public bool Success => FailedEvaluations == 0 && ErroredEvaluations == 0;
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
                if (testDeclaration.DeclaringTest.IsTargetless)
                {
                    testResults.AddRange(await RunSelectedTargetsAsync(testFileModel, testDeclaration, cases));
                }
                else if (testDeclaration.TryGetSemanticModel().IsSuccess(out var semanticModel, out var _) &&
                    semanticModel is SemanticModel testSemanticModel)
                {
                    // A literal target names one file, so the test file's own directory is the frame of
                    // reference its facts are reported in.
                    var factRoot = testFileModel.SourceFile.FileHandle.GetParent().Uri;

                    foreach (var inputCase in cases)
                    {
                        testResults.Add(Timed(() => Evaluate(testFileModel, testDeclaration, testSemanticModel, factRoot, inputCase)));
                    }
                }
            }

            return new TestResults(testResults.ToImmutable());
        }

        /// <summary>
        /// Expands a body-owned selector into its targets and evaluates each one independently, so that
        /// a target which fails to compile or bind never hides the outcome of the others.
        ///
        /// A target is compiled once and then evaluated against every case. Compilation depends only on
        /// the target: a case supplies parameters to the emitted template, and can change neither which
        /// targets are selected nor how one of them compiles. Results are still returned case by case,
        /// so doing the work target-major does not change the order anything is reported in.
        /// </summary>
        private async Task<IEnumerable<TestResult>> RunSelectedTargetsAsync(SemanticModel testFileModel, TestSymbol testDeclaration, TestInputCase?[] cases)
        {
            var testFileHandle = testFileModel.SourceFile.FileHandle;
            var testFileUri = testFileHandle.Uri;

            if (TestTargetSelectorBinder.TryBind(testDeclaration.DeclaringTest) is not { } selector)
            {
                return [.. cases.Select(inputCase => Timed(() => Unevaluated(testFileUri, testDeclaration, testFileUri, "The test declares no usable 'match' selector.", inputCase)))];
            }

            var discovery = TestTargetDiscovery.Discover(testFileHandle.GetParent(), selector);

            if (discovery.Error is { } error)
            {
                return [.. cases.Select(inputCase => Timed(() => Unevaluated(testFileUri, testDeclaration, testFileUri, error.Message, inputCase)))];
            }

            var factRoot = discovery.Root ?? testFileHandle.GetParent().Uri;
            // One bucket per case, each holding that case's results in target order, so that what is
            // reported stays case-major even though the work is now done target-major.
            var resultsByCase = cases.Select(_ => new List<TestResult>()).ToArray();

            foreach (var targetUri in discovery.Targets)
            {
                var compileStart = Stopwatch.GetTimestamp();
                var compiled = await CompileTargetAsync(targetUri);
                var compileDuration = Stopwatch.GetElapsedTime(compileStart);

                for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
                {
                    var inputCase = cases[caseIndex];
                    var result = Timed(() => EvaluateCompiledTarget(testFileModel, testDeclaration, compiled, targetUri, factRoot, inputCase));

                    // The compilation is charged to the case that triggered it and to no other, so the
                    // durations still sum to the work actually done rather than counting it once a case.
                    resultsByCase[caseIndex].Add(result with { Duration = result.Duration + compileDuration });
                    compileDuration = TimeSpan.Zero;
                }
            }

            return resultsByCase.SelectMany(results => results);
        }

        /// <summary>
        /// Measures one evaluation. Centralized so that every result carries a real measurement:
        /// attaching the duration at each construction site would let a new path forget to.
        /// </summary>
        private static TestResult Timed(Func<TestResult> evaluate)
        {
            var start = Stopwatch.GetTimestamp();
            var result = evaluate();

            return result with { Duration = Stopwatch.GetElapsedTime(start) };
        }

        /// <summary>
        /// A target compiled once, ready to be evaluated against any number of cases. Carries the reason
        /// instead of a model when the target could not be compiled, so that every case reports that
        /// failure rather than the first case absorbing it.
        /// </summary>
        private record CompiledTarget(SemanticModel? Model, string? Error);

        private async Task<CompiledTarget> CompileTargetAsync(IOUri targetUri)
        {
            SemanticModel targetModel;

            try
            {
                var targetCompilation = await compiler.CreateCompilation(targetUri, skipRestore: true);
                targetModel = targetCompilation.GetEntrypointSemanticModel();
            }
            catch (Exception exception)
            {
                return new CompiledTarget(null, SanitizeEvaluationError(exception));
            }

            if (targetModel.HasErrors())
            {
                return new CompiledTarget(null, "The target has compilation errors and cannot be evaluated.");
            }

            return new CompiledTarget(targetModel, null);
        }

        private static TestResult EvaluateCompiledTarget(SemanticModel testFileModel, TestSymbol testDeclaration, CompiledTarget compiled, IOUri targetUri, IOUri factRoot, TestInputCase? inputCase)
        {
            var testFileUri = testFileModel.SourceFile.FileHandle.Uri;

            if (compiled.Model is not { } targetModel)
            {
                return Unevaluated(testFileUri, testDeclaration, targetUri, compiled.Error ?? "The target could not be compiled.", inputCase);
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
                var mocks = GetMocks(testFileModel, inputCase);

                // Built here but not used unless an assertion actually reads an evaluated value: source
                // policies must keep working without any deployment inputs.
                var evaluated = new TestEvaluatedFactsProvider(
                    targetModel,
                    () => TryGetParameters(testFileModel, testDeclaration, inputCase),
                    inputCase?.Context,
                    facts,
                    targetModel.SourceFile.FileHandle.Uri.GetPathRelativeTo(factRoot),
                    mocks);

                var allAssertions = TestAssertionEvaluator.Evaluate(testFileModel, testDeclaration.DeclaringTest, facts, inputCase, evaluated);
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
                var mocks = GetMocks(testFileModel, inputCase);
                var templateJToken = TestTemplateEmitter.Emit(targetModel);
                var context = inputCase?.Context ?? TestDeploymentContext.Empty;
                var template = TemplateEvaluator.Evaluate(templateJToken, parameters, configBuilder: configuration => TestMockRegistryExtensions.Apply(mocks, context.Apply(configuration)));
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
        /// Builds the mocks in effect for one case. The definitions are ordinary Bicep owned by the test
        /// file, so they are evaluated per case: a definition may read the case's own inputs, and each
        /// case gets its own immutable registry rather than sharing mutable state with another.
        /// </summary>
        private static TestMockRegistry GetMocks(SemanticModel testFileModel, TestInputCase? inputCase)
        {
            if (testFileModel.Root.Syntax.Children.OfType<MocksDeclarationSyntax>().FirstOrDefault() is not { } declaration)
            {
                return TestMockRegistry.Empty;
            }

            var evaluated = BicepValueEvaluator.Evaluate(
                new EmitterContext(testFileModel),
                declaration.Value,
                "object",
                inputValues: inputCase?.Values,
                deploymentContext: inputCase?.Context);

            return TestMockRegistry.FromObject(evaluated as JObject);
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

            return BicepValueEvaluator.Translate(lineBreak < 0 ? message : message[..lineBreak].TrimEnd());
        }

        private static TestResult Unevaluated(IOUri testFileUri, TestSymbol testDeclaration, IOUri targetUri, string error, TestInputCase? inputCase = null)
            => new(testDeclaration, new TestCaseIdentity(testFileUri, testDeclaration.Name, targetUri, inputCase), new TestEvaluation(null, error, [], []));



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
