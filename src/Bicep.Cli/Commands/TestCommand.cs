// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.CommandLine;
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
        private const string SkippedSymbol = "[-]";

        private readonly ILogger logger;
        private readonly IOContext io;
        private readonly DiagnosticLogger diagnosticLogger;
        private readonly BicepCompiler compiler;
        private readonly IFeatureProviderFactory featureProviderFactory;
        private readonly InputOutputArgumentsResolver inputOutputArgumentsResolver;

        public TestCommand(
            IOContext io,
            ILogger logger,
            DiagnosticLogger diagnosticLogger,
            BicepCompiler compiler,
            IFeatureProviderFactory featureProviderFactory,
            InputOutputArgumentsResolver inputOutputArgumentsResolver)
        {
            this.logger = logger;
            this.diagnosticLogger = diagnosticLogger;
            this.compiler = compiler;
            this.featureProviderFactory = featureProviderFactory;
            this.io = io;
            this.inputOutputArgumentsResolver = inputOutputArgumentsResolver;
        }

        public async Task<int> RunAsync(TestArguments args)
        {
            // Sorted so that a pattern covering several files reports them in the same order every run.
            var inputUris = this.inputOutputArgumentsResolver.ResolveFilePatternInputArguments(args)
                .OrderBy(uri => uri.ToString(), IOUri.GlobalSettings.LocalFilePathComparer)
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
            var json = args.OutputFormat == TestOutputFormat.Json;
            var allResults = ImmutableArray.CreateBuilder<TestResult>();
            var allInventoryEntries = ImmutableArray.CreateBuilder<TestInventoryEntry>();

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
                    var (listHasErrors, inventory) = await ListAsync(args, inputUri, json);

                    hasErrors |= listHasErrors;
                    allInventoryEntries.AddRange(inventory.Entries);
                    continue;
                }

                var compilation = await compiler.CreateCompilation(inputUri, skipRestore: args.NoRestore);
                var summary = diagnosticLogger.LogDiagnostics(GetDiagnosticOptions(args), compilation);
                var testResults = await new TestRunner(compiler).RunAsync(compilation.GetEntrypointSemanticModel());

                if (!json)
                {
                    LogResults(testResults, qualifyWithTestFile: inputUris.Length > 1);
                }

                allResults.AddRange(testResults.Results);

                hasErrors |= summary.HasErrors;
            }

            if (args.List)
            {
                if (json)
                {
                    await io.Output.Writer.WriteLineAsync(TestReportSerializer.SerializeInventory(allInventoryEntries));
                }
            }
            else
            {
                var aggregated = new TestResults(allResults.ToImmutable());

                if (json)
                {
                    await io.Output.Writer.WriteLineAsync(TestReportSerializer.SerializeResults(aggregated));
                    hasErrors |= !aggregated.Success;
                }
                else
                {
                    // A single summary covers every discovered file, so that one failing file is never
                    // followed by a later file reporting overall success.
                    hasErrors |= LogSummary(aggregated, hasErrors);
                }
            }

            return hasErrors ? 1 : 0;
        }

        /// <summary>
        /// Reports what a test file covers without compiling, restoring or evaluating any target.
        /// Listing confirms inventory; it never claims the targets compile or pass.
        /// </summary>
        private async Task<(bool hasErrors, TestInventory inventory)> ListAsync(TestArguments args, IOUri inputUri, bool json)
        {
            var compilation = await compiler.CreateCompilation(inputUri, skipRestore: true);
            var summary = diagnosticLogger.LogDiagnostics(GetDiagnosticOptions(args), compilation);
            var inventory = TestDiscoveryService.Discover(compilation.GetEntrypointSemanticModel());

            if (!json)
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

        private void LogResults(TestResults testResults, bool qualifyWithTestFile)
        {
            foreach (var (testDeclaration, identity, evaluation) in testResults.Results)
            {
                // A test may resolve to several targets, so the target is always named: without it two
                // outcomes of the same declaration would be indistinguishable. When several test files
                // run together the file is named too, since test names are only unique within a file.
                var name = qualifyWithTestFile
                    ? $"{identity.TestFileName}: {testDeclaration.Name}"
                    : testDeclaration.Name;
                var label = identity.IsSelfTargeted
                    ? name
                    : $"{name} ({identity.RelativeTargetPath})";

                if (evaluation.Success)
                {
                    io.Output.Writer.WriteLine($"{SuccessSymbol} Evaluation {label} Passed!");
                }
                else if (evaluation.Skip)
                {
                    io.Error.Writer.WriteLine($"{SkippedSymbol} Evaluation {label} Skipped!");
                    io.Error.Writer.WriteLine($"Reason: {evaluation.Error}");
                }
                else
                {
                    io.Error.Writer.WriteLine($"{FailureSymbol} Evaluation {label} Failed at {evaluation.FailedAssertions.Length} / {evaluation.AllAssertions.Length} assertions!");
                    foreach (var assertion in evaluation.FailedAssertions)
                    {
                        io.Error.Writer.WriteLine($"\t{FailureSymbol} Assertion {assertion.Source} failed!");

                        if (assertion.Message is { } message)
                        {
                            io.Error.Writer.WriteLine($"\t\t{message}");
                        }

                        if (assertion.Error is { } assertionError)
                        {
                            io.Error.Writer.WriteLine($"\t\tCould not be evaluated: {assertionError}");
                        }

                        // Naming the offending declarations is what makes a source policy actionable;
                        // a count alone leaves the author to find them by hand.
                        foreach (var violation in assertion.Violations)
                        {
                            io.Error.Writer.WriteLine($"\t\t{violation}");
                        }
                    }
                }
            }
        }

        /// <summary>Reports the aggregate outcome once, and returns whether it was a failure.</summary>
        private bool LogSummary(TestResults testResults, bool hasCompilationErrors)
        {
            // Do not report overall success when compilation diagnostics contain errors.
            if (testResults.Success && !hasCompilationErrors)
            {
                io.Output.Writer.WriteLine($"All {testResults.TotalEvaluations} evaluations passed!");

                return false;
            }

            if (!testResults.Success)
            {
                io.Error.Writer.WriteLine($"Evaluation Summary: Failure!");
                io.Error.Writer.WriteLine($"Total: {testResults.TotalEvaluations} - Success: {testResults.SuccessfulEvaluations} - Skipped: {testResults.SkippedEvaluations} - Failed: {testResults.FailedEvaluations}");

                return true;
            }

            return false;
        }

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
                Description = "The path to the input .bicep or .biceptest file.",
                Arity = ArgumentArity.ZeroOrOne,
            };
            var filePatternOption = new System.CommandLine.Option<string?>(Option.Pattern)
            {
                Description = "Runs tests in all files matching the specified glob pattern, relative to the current directory.",
            };
            var listOption = new System.CommandLine.Option<bool>(Option.List)
            {
                Description = "Lists the tests and targets that would run, without evaluating them.",
            };
            var outputFormatOption = new System.CommandLine.Option<TestOutputFormat?>(Option.OutputFormat)
            {
                Description = "Set the format of test output (Default, Json). Json writes a machine-readable document to stdout and keeps progress text on stderr.",
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
            command.Add(filePatternOption);
            command.Add(listOption);
            command.Add(outputFormatOption);
            command.Add(noRestoreOption);
            command.Add(diagnosticsFormatOption);
            command.Validators.Add((System.CommandLine.Parsing.CommandResult result) => CommandLineBuilderContext.ValidatePositionalArgument(result, inputFileArgument));

            command.SetAction((result, ct) => context.RunCommandAsync(async () =>
            {
                var args = new TestArguments(
                    result.GetValue(inputFileArgument),
                    result.GetValue(filePatternOption),
                    result.GetValue(noRestoreOption),
                    result.GetValue(listOption),
                    result.GetValue(outputFormatOption),
                    result.GetValue(diagnosticsFormatOption));

                return await context.GetCommand<TestCommand>().RunAsync(args);
            }));

            return command;
        }
    }
}
