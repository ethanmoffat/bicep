// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Abstractions;
using Bicep.Cli.Arguments;
using Bicep.Cli.Constants;
using Bicep.Cli.Helpers;
using Bicep.Cli.Logging;
using Bicep.Cli.Services;
using Bicep.Core;
using Bicep.Core.Features;
using Bicep.IO.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Option = Bicep.Cli.Constants.Option;

namespace Bicep.Cli.Commands
{
    public class TestCommand : ICommand
    {
        private const string SuccessSymbol = "[✓]";
        private const string FailureSymbol = "[✗]";
        private const string ErrorSymbol = "[!]";

        /// <summary>
        /// How far a case is indented under the heading that groups it. Spaces rather than a tab so the
        /// tree lines up the same however a terminal or log viewer renders tab stops.
        /// </summary>
        private const string GroupIndent = "  ";

        private readonly ILogger logger;
        private readonly IOContext io;
        private readonly DiagnosticLogger diagnosticLogger;
        private readonly BicepCompiler compiler;
        private readonly IFeatureProviderFactory featureProviderFactory;
        private readonly InputOutputArgumentsResolver inputOutputArgumentsResolver;
        private readonly IFileSystem fileSystem;
        private readonly OutputWriter outputWriter;

        public TestCommand(
            IOContext io,
            ILogger logger,
            DiagnosticLogger diagnosticLogger,
            BicepCompiler compiler,
            IFeatureProviderFactory featureProviderFactory,
            InputOutputArgumentsResolver inputOutputArgumentsResolver,
            IFileSystem fileSystem,
            OutputWriter outputWriter)
        {
            this.logger = logger;
            this.diagnosticLogger = diagnosticLogger;
            this.compiler = compiler;
            this.featureProviderFactory = featureProviderFactory;
            this.io = io;
            this.inputOutputArgumentsResolver = inputOutputArgumentsResolver;
            this.fileSystem = fileSystem;
            this.outputWriter = outputWriter;
        }

        public async Task<int> RunAsync(TestArguments args)
        {
            // Listing evaluates nothing, so there are no results to report. Emitting a JUnit document
            // of cases that never ran would publish an inventory as if it were a passing test run.
            if (args.List && args.OutputFormat == TestOutputFormat.JUnit)
            {
                await io.Error.Writer.WriteLineAsync($"{Option.List} does not produce test results, so it cannot be reported as JUnit. Use \"{Option.OutputFormat} Json\" to list in a machine-readable form.");

                return 1;
            }

            // A results file has to be written in a stated format. Guessing one - from the extension,
            // or from a default that may later change - would silently write a document a pipeline
            // cannot parse.
            if (args.ResultsFile is not null && args.OutputFormat is null or TestOutputFormat.Default)
            {
                await io.Error.Writer.WriteLineAsync($"{Option.ResultsFile} requires \"{Option.OutputFormat} Json\" or \"{Option.OutputFormat} JUnit\".");

                return 1;
            }

            // Checked here rather than left to the shared resolver, whose message names --pattern: an
            // option this command no longer has, because its positional argument takes both.
            if (args.InputFile is null && args.FilePattern is null)
            {
                await io.Error.Writer.WriteLineAsync("The path to a .bicep or .biceptest file, or a glob pattern matching them, must be specified.");

                return 1;
            }

            // Sorted so that a pattern covering several files reports them in the same order every
            // run, and in the same order on every host: ordinal rather than the host's case rules.
            var inputUris = this.inputOutputArgumentsResolver.ResolveFilePatternInputArguments(args)
                .OrderBy(uri => uri.ToString(), StringComparer.Ordinal)
                .ToArray();

            // A pattern that discovers nothing is an error: an empty suite must never be reported as a
            // suite that passed. An explicitly named file keeps its existing behavior.
            if (args.FilePattern is not null && inputUris.Length == 0)
            {
                await io.Error.Writer.WriteLineAsync($"The pattern \"{args.FilePattern}\" did not match any test files.");

                return 1;
            }

            var hasErrors = false;
            var warnedAboutExperimentalFeature = false;
            var outputDetail = args.OutputDetail ?? TestOutputDetail.Failures;
            var json = args.OutputFormat == TestOutputFormat.Json;
            // Both machine-readable formats keep stdout for the document and progress text on stderr,
            // so a host can parse stdout even when the command exits non-zero.
            var machineReadable = json || args.OutputFormat == TestOutputFormat.JUnit;
            var resultsFileUri = args.ResultsFile is { } resultsFile
                ? inputOutputArgumentsResolver.PathToUri(resultsFile)
                : (IOUri?)null;
            // The format says which document to produce; the results file says where to put it. When
            // it goes to a file, stdout is free to carry the ordinary human log - which is what a
            // pipeline wants: a readable log for people and a parseable file for the build system.
            var documentToStdout = machineReadable && resultsFileUri is null;
            var allResults = ImmutableArray.CreateBuilder<TestResult>();
            var allInventoryEntries = ImmutableArray.CreateBuilder<TestInventoryEntry>();
            // Which input files turned out to belong to something that was run. An input passed over
            // for every discovered test file named a test nobody ran, which is a silent loss of cases.
            var inputsThatBound = new HashSet<IOUri>();

            foreach (var inputUri in inputUris)
            {
                ArgumentHelper.ValidateBicepOrBicepTestFile(inputUri);

                if (!featureProviderFactory.GetFeatureProvider(inputUri).TestFrameworkEnabled)
                {
                    await io.Error.Writer.WriteLineAsync("TestFrameWork not enabled");

                    return 1;
                }

                // Warn once per invocation rather than once per file, so that a pattern covering many
                // test files does not bury its own output in repeated disclaimers.
                if (!warnedAboutExperimentalFeature)
                {
                    logger.LogWarning(string.Format(CliResources.ExperimentalFeaturesDisclaimerMessage, "TestFramework"));
                    warnedAboutExperimentalFeature = true;
                }

                if (args.List)
                {
                    var (listHasErrors, inventory) = await ListAsync(args, inputUri, documentToStdout);

                    hasErrors |= listHasErrors;
                    allInventoryEntries.AddRange(inventory.Entries);
                    continue;
                }

                var compilation = await compiler.CreateCompilation(inputUri, skipRestore: args.NoRestore);
                var summary = diagnosticLogger.LogDiagnostics(GetDiagnosticOptions(args), compilation);
                var (inputCases, inputErrors, boundInputs) = await LoadInputCasesAsync(args, inputUri, discovered: args.FilePattern is not null);

                inputsThatBound.UnionWith(boundInputs);

                // A broken input file never stops the remaining ones from running: every case that can
                // be evaluated still is, and the failure is reported and folded into the exit code.
                foreach (var inputError in inputErrors)
                {
                    await io.Error.Writer.WriteLineAsync(inputError);
                }

                hasErrors |= inputErrors.Length > 0;

                var testResults = await new TestRunner(compiler).RunAsync(compilation.GetEntrypointSemanticModel(), inputCases);

                if (!documentToStdout)
                {
                    LogResults(testResults, qualifyWithTestFile: inputUris.Length > 1, outputDetail);
                }

                allResults.AddRange(testResults.Results);

                hasErrors |= summary.HasErrors;
            }

            if (args.List)
            {
                if (json)
                {
                    await EmitAsync(TestReportSerializer.SerializeInventory(allInventoryEntries), resultsFileUri);
                }
            }
            else
            {
                // An input file that bound to none of the discovered test files contributed no cases at
                // all. Nothing above reported it, because each individual file legitimately passed it
                // over, so the loss would otherwise be invisible.
                if (args.FilePattern is not null && !args.Inputs.IsDefaultOrEmpty)
                {
                    foreach (var input in args.Inputs)
                    {
                        var unboundUri = inputOutputArgumentsResolver.PathToUri(input);

                        if (!inputsThatBound.Contains(unboundUri))
                        {
                            await io.Error.Writer.WriteLineAsync($"{unboundUri.GetFileName()}: the test file this input supplies cases for was not among the files being run, so none of its cases ran.");
                            hasErrors = true;
                        }
                    }
                }

                var aggregated = new TestResults(allResults.ToImmutable());

                if (!documentToStdout)
                {
                    // A single summary covers every discovered file, so that one failing file is never
                    // followed by a later file reporting overall success.
                    hasErrors |= LogSummary(aggregated, hasErrors);
                }

                if (machineReadable)
                {
                    await EmitAsync(json
                        ? TestReportSerializer.SerializeResults(aggregated)
                        : TestJUnitSerializer.SerializeResults(aggregated, fileSystem.Directory.GetCurrentDirectory()),
                        resultsFileUri);

                    hasErrors |= !aggregated.Success;
                }
            }

            return hasErrors ? 1 : 0;
        }

        /// <summary>
        /// Writes the machine-readable document where it was asked for. A results file is written even
        /// when the run failed: a pipeline that only gets results from a passing run cannot report what
        /// went wrong.
        /// </summary>
        private async Task EmitAsync(string document, IOUri? resultsFileUri)
        {
            if (resultsFileUri is { } uri)
            {
                await outputWriter.WriteToFileAsync(uri, document);
            }
            else
            {
                await io.Output.Writer.WriteLineAsync(document);
            }
        }

        /// <summary>
        /// Compiles each supplied parameters file and collects the cases it declares for this test file.
        /// A file that cannot contribute cases is reported and skipped rather than aborting the run, so
        /// one bad input file never hides the outcome of the others.
        ///
        /// An input file binds to exactly one test file by name. A glob asks for whatever it matches, so
        /// a file bound to one of the other matches is simply not this file's input and is passed over:
        /// requiring every input to bind to every discovered file would make cases and discovery
        /// mutually exclusive. The caller checks afterwards that each input bound to something. A test
        /// file named outright is different - there is only one file the input could have meant.
        /// </summary>
        private async Task<(ImmutableArray<TestInputCase> Cases, ImmutableArray<string> Errors, ImmutableArray<IOUri> Bound)> LoadInputCasesAsync(TestArguments args, IOUri testFileUri, bool discovered)
        {
            if (args.Inputs.IsDefaultOrEmpty)
            {
                return ([], [], []);
            }

            var cases = ImmutableArray.CreateBuilder<TestInputCase>();
            var errors = ImmutableArray.CreateBuilder<string>();
            var bound = ImmutableArray.CreateBuilder<IOUri>();

            foreach (var input in args.Inputs)
            {
                var inputUri = inputOutputArgumentsResolver.PathToUri(input);

                ArgumentHelper.ValidateBicepTestParamFile(inputUri);

                var compilation = await compiler.CreateCompilation(inputUri, skipRestore: args.NoRestore);
                var result = TestInputCaseLoader.Load(compilation.GetEntrypointSemanticModel(), testFileUri);

                if (result.Error is { } error)
                {
                    if (discovered && result.BoundTestFile is not null)
                    {
                        continue;
                    }

                    diagnosticLogger.LogDiagnostics(GetDiagnosticOptions(args), compilation);
                    errors.Add($"{inputUri.GetFileName()}: {error}");
                    continue;
                }

                bound.Add(inputUri);
                cases.AddRange(result.Cases);
            }

            return (cases.ToImmutable(), errors.ToImmutable(), bound.ToImmutable());
        }

        /// <summary>
        /// Reports what a test file covers without compiling, restoring or evaluating any target.
        /// Listing confirms inventory; it never claims the targets compile or pass.
        /// </summary>
        private async Task<(bool hasErrors, TestInventory inventory)> ListAsync(TestArguments args, IOUri inputUri, bool documentToStdout)
        {
            var compilation = await compiler.CreateCompilation(inputUri, skipRestore: true);
            var summary = diagnosticLogger.LogDiagnostics(GetDiagnosticOptions(args), compilation);
            var inventory = TestDiscoveryService.Discover(compilation.GetEntrypointSemanticModel());

            if (!documentToStdout)
            {
                foreach (var entry in inventory.Entries)
                {
                    if (entry.IsResolved)
                    {
                        await io.Output.Writer.WriteLineAsync($"{entry.Identity.TestFileName}: {entry.Identity.TestName} -> {entry.Identity.RelativeTargetPath}");
                    }
                    else
                    {
                        await io.Error.Writer.WriteLineAsync($"{entry.Identity.TestFileName}: {entry.Identity.TestName} -> (no targets): {entry.Error}");
                    }
                }
            }

            foreach (var skipped in inventory.SkippedDirectories)
            {
                await io.Error.Writer.WriteLineAsync($"Not traversed (link): {skipped}");
            }

            return (summary.HasErrors || inventory.HasErrors, inventory);
        }

        private void LogResults(TestResults testResults, bool qualifyWithTestFile, TestOutputDetail detail)
        {
            if (detail is TestOutputDetail.Summary)
            {
                return;
            }

            // Cases of the same test and target are reported together, because they are variations of
            // one thing rather than unrelated outcomes. Grouping preserves the order results arrived
            // in, so the cases under a heading stay in the order the input file declared them.
            foreach (var group in testResults.Results.GroupBy(x => (x.Identity.TestFile, x.Identity.TestName, x.Identity.TargetFile)))
            {
                var members = group.ToArray();
                // A passing case says only that nothing is wrong with it, so it is reported only when
                // the whole log was asked for. What went wrong is never filtered out.
                var reported = detail is TestOutputDetail.All
                    ? members
                    : [.. members.Where(x => !x.Result.Success)];

                if (reported.Length == 0)
                {
                    continue;
                }

                if (members.Length < 2)
                {
                    foreach (var result in reported)
                    {
                        LogResult(result, indent: string.Empty, $"Evaluation {DescribeCase(result, qualifyWithTestFile)}");
                    }

                    continue;
                }

                LogCaseGroup(members, reported, qualifyWithTestFile);
            }
        }

        /// <summary>
        /// Reports several cases of one test and target under a single heading, so that variations of
        /// one thing read as one thing. The heading carries the tally, because with the cases indented
        /// beneath it the reader would otherwise have to count them.
        ///
        /// The whole group goes to one stream, chosen by whether any of its cases did not pass. Sending
        /// each line to the stream its own outcome implies would tear the group in half whenever the
        /// two streams are redirected separately, leaving a heading with no cases under it.
        /// </summary>
        private void LogCaseGroup(TestResult[] members, TestResult[] reported, bool qualifyWithTestFile)
        {
            var passed = members.Count(x => x.Result.Success);
            var writer = passed == members.Length ? io.Output.Writer : io.Error.Writer;

            writer.WriteLine($"Evaluation {DescribeTarget(members[0], qualifyWithTestFile)} - {passed}/{members.Length} cases passed");

            foreach (var result in reported)
            {
                // Only what distinguishes this case from its siblings is left to say: the heading above
                // has already named the test and the target.
                var label = result.Identity.Inputs is { } inputs
                    ? $"Case {inputs.InputFileName}: {inputs.Name}"
                    : $"Case {DescribeCase(result, qualifyWithTestFile)}";

                LogResult(result, GroupIndent, label, writer);
            }
        }

        /// <summary>
        /// Names the test and target a result belongs to. When several test files run together the file
        /// is named too, since test names are only unique within a file. The target is always named:
        /// without it two outcomes of the same declaration would be indistinguishable.
        /// </summary>
        private static string DescribeTarget(TestResult result, bool qualifyWithTestFile)
        {
            var identity = result.Identity;
            var name = qualifyWithTestFile
                ? $"{identity.TestFileName}: {result.Source.Name}"
                : result.Source.Name;

            return identity.IsSelfTargeted ? name : $"{name} ({identity.RelativeTargetPath})";
        }

        /// <summary>
        /// What to call one result. Under a heading that has already named the test and target, only
        /// what distinguishes this case from its siblings is left: the values it ran with.
        /// </summary>
        private static string DescribeCase(TestResult result, bool qualifyWithTestFile)
        {
            var target = DescribeTarget(result, qualifyWithTestFile);

            // The case is named so that two outcomes of the same test and target, differing only in
            // the values they ran with, are never reported as the same thing.
            return result.Identity.Inputs is { } inputs
                ? $"{target} [{inputs.InputFileName}: {inputs.Name}]"
                : target;
        }

        private void LogResult(TestResult result, string indent, string label, TextWriter? groupWriter = null)
        {
            var evaluation = result.Result;

            if (evaluation.Success)
            {
                (groupWriter ?? io.Output.Writer).WriteLine($"{indent}{SuccessSymbol} {label} Passed!");

                return;
            }

            var writer = groupWriter ?? io.Error.Writer;

            if (evaluation.Errored)
            {
                writer.WriteLine($"{indent}{ErrorSymbol} {label} could not be evaluated!");
                writer.WriteLine($"{indent}Reason: {evaluation.Error}");

                return;
            }

            writer.WriteLine($"{indent}{FailureSymbol} {label} Failed at {evaluation.FailedAssertions.Length} / {evaluation.AllAssertions.Length} assertions!");

            // An ungrouped result keeps the tabs it has always used, so its output is unchanged. Inside
            // a group everything is spaces, because a tab following the group's own indent lands on a
            // tab stop that has nothing to do with the tree it is supposed to line up with.
            var assertionIndent = indent.Length == 0 ? "\t" : $"{indent}  ";
            var detailIndent = indent.Length == 0 ? "\t\t" : $"{indent}    ";

            foreach (var assertion in evaluation.FailedAssertions)
            {
                writer.WriteLine($"{assertionIndent}{FailureSymbol} Assertion {assertion.Source} failed!");

                if (assertion.Message is { } message)
                {
                    writer.WriteLine($"{detailIndent}{message}");
                }

                if (assertion.Error is { } assertionError)
                {
                    writer.WriteLine($"{detailIndent}Could not be evaluated: {assertionError}");
                }

                // Naming the offending declarations is what makes a source policy actionable;
                // a count alone leaves the author to find them by hand.
                foreach (var violation in assertion.Violations)
                {
                    writer.WriteLine($"{detailIndent}{violation}");
                }
            }
        }

        /// <summary>
        /// Reports the aggregate outcome once, and returns whether it was a failure.
        ///
        /// One line in one shape whatever the outcome, so that a reader - or a log scraper - never has
        /// to recognize two different summaries. Errored cases are counted apart from failed ones
        /// because they call for different action: a failure means a policy was broken, an error means
        /// the policy never ran against that target. Either one makes the run a failure.
        /// </summary>
        private bool LogSummary(TestResults testResults, bool hasCompilationErrors)
        {
            var passed = testResults.Success && !hasCompilationErrors;
            var verdict = passed ? "Passed!" : "Failed!";
            var line = $"{verdict} - Failed: {testResults.FailedEvaluations}, Errored: {testResults.ErroredEvaluations}, Passed: {testResults.SuccessfulEvaluations}, Total: {testResults.TotalEvaluations}, Duration: {FormatDuration(testResults.TotalDuration)}";

            if (passed)
            {
                io.Output.Writer.WriteLine(line);

                return false;
            }

            io.Error.Writer.WriteLine(line);

            // Without this, a run whose every evaluation passed but whose test file did not compile
            // reports zeros beside a failing verdict, which reads as a contradiction.
            if (testResults.Success && hasCompilationErrors)
            {
                io.Error.Writer.WriteLine("The run failed because errors were reported above, not because an evaluation did.");
            }

            return !testResults.Success;
        }

        /// <summary>
        /// The summed cost of the run, in the shape <c>dotnet test</c> reports: the largest unit that
        /// says something, and the next one down.
        /// </summary>
        private static string FormatDuration(TimeSpan duration)
            => duration.TotalSeconds < 1
                ? $"{duration.Milliseconds}ms"
                : duration.TotalMinutes < 1
                    ? $"{duration.Seconds}s {duration.Milliseconds}ms"
                    : $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

        private DiagnosticOptions GetDiagnosticOptions(TestArguments args)
            => new(
                Format: args.DiagnosticsFormat ?? DiagnosticsFormat.Default,
                SarifToStdout: false);

        internal static System.CommandLine.Command CreateCommand(CommandLineBuilderContext context)
        {
            var command = new System.CommandLine.Command(Constants.Command.Test, "Runs tests in a .bicep or .biceptest file.")
            {
                TreatUnmatchedTokensAsErrors = true,
            };

            var inputFileArgument = new System.CommandLine.Argument<string?>(Constants.Argument.InputFile)
            {
                Description = "The path to a .bicep or .biceptest file, or a glob pattern matching them relative to the current directory.",
                Arity = ArgumentArity.ZeroOrOne,
            };
            var inputsOption = new System.CommandLine.Option<string[]>(Option.Inputs)
            {
                Description = "Runs the tests once per case declared in the specified .biceptestparam file. Each input file applies to the test file it names. May be specified more than once.",
                AllowMultipleArgumentsPerToken = true,
            };
            var listOption = new System.CommandLine.Option<bool>(Option.List)
            {
                Description = "Lists the tests and targets that would run, without evaluating them.",
            };
            var outputFormatOption = new System.CommandLine.Option<TestOutputFormat?>(Option.OutputFormat)
            {
                Description = "Set the format of test output (Default, Json, JUnit). Json and JUnit write a machine-readable document to stdout and keep progress text on stderr.",
            };
            var outputDetailOption = new System.CommandLine.Option<TestOutputDetail?>(Option.OutputDetail)
            {
                Description = "Set how much of the run is written to the console (Summary, Failures, All). Defaults to Failures: the counts line plus every case that failed or could not be evaluated.",
            };
            var resultsFileOption = new System.CommandLine.Option<string?>(Option.ResultsFile)
            {
                Description = "Write the machine-readable document to the specified file instead of stdout, leaving stdout for progress text. Requires --output-format Json or JUnit.",
            };
            var noRestoreOption = new System.CommandLine.Option<bool>(Option.NoRestore)
            {
                Description = "Do not restore modules prior to running tests.",
            };
            var diagnosticsFormatOption = new System.CommandLine.Option<DiagnosticsFormat?>(Option.DiagnosticsFormat)
            {
                Description = "Set the format of diagnostics (Default, SARIF).",
            };

            command.Add(inputFileArgument);
            command.Add(inputsOption);
            command.Add(listOption);
            command.Add(outputFormatOption);
            command.Add(outputDetailOption);
            command.Add(resultsFileOption);
            command.Add(noRestoreOption);
            command.Add(diagnosticsFormatOption);
            command.Validators.Add((System.CommandLine.Parsing.CommandResult result) => CommandLineBuilderContext.ValidatePositionalArgument(result, inputFileArgument));

            command.SetAction((result, ct) => context.RunCommandAsync(async () =>
            {
                var input = result.GetValue(inputFileArgument);
                var isPattern = input is not null && TestArguments.IsPattern(input);

                var args = new TestArguments(
                    isPattern ? null : input,
                    isPattern ? input : null,
                    result.GetValue(noRestoreOption),
                    result.GetValue(listOption),
                    [.. result.GetValue(inputsOption) ?? []],
                    result.GetValue(outputFormatOption),
                    result.GetValue(outputDetailOption),
                    result.GetValue(resultsFileOption),
                    result.GetValue(diagnosticsFormatOption));

                return await context.GetCommand<TestCommand>().RunAsync(args);
            }));

            return command;
        }
    }
}
