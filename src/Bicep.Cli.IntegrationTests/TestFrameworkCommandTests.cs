// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;
using Bicep.Core;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.IntegrationTests
{
    [TestClass]
    public class TestFrameworkCommandTests : TestBase
    {
        [TestMethod]
        public async Task Test_ZeroFiles_ShouldFail_WithExpectedErrorMessage()
        {
            var (output, error, result) = await Bicep("test");

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain($"Either the input file path or the --pattern parameter must be specified");
            }
        }

        [TestMethod]
        public async Task Test_NonBicepFiles_ShouldFail_WithExpectedErrorMessage()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var (output, error, result) = await Bicep(settings, "test", "/dev/zero");

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain($@"The specified input ""{Path.GetFullPath("/dev/zero")}"" was not recognized as a Bicep or Bicep test file. Valid files must use either the {LanguageConstants.LanguageFileExtension} or {LanguageConstants.TestFileExtension} extension.");
            }
        }

        [TestMethod]
        public async Task Test_BicepTestFile_ShouldRunTests()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param value int = 1
                assert isPositive = value > 0
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", """
                test passing 'main.bicep' = {
                  params: {
                    value: 5
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation passing (main.bicep) Passed!");
                output.Should().Contain("All 1 evaluations passed!");
            }
        }

        [TestMethod]
        public async Task Test_BicepTestFile_WithFailingAssertion_ShouldFail()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param value int = 1
                assert isPositive = value > 0
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", """
                test failing 'main.bicep' = {
                  params: {
                    value: -5
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Evaluation failing (main.bicep) Failed");
                error.Should().Contain("Assertion isPositive failed!");
            }
        }

        [TestMethod]
        public async Task Test_BicepTestFile_WithoutTestFrameworkEnabled_ShouldFail()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: false), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", "// empty", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("TestFrameWork not enabled");
            }
        }

        [TestMethod]
        public async Task Test_CompilationErrors_ShouldFail_WithoutReportingOverallSuccess()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            // Include a valid test to verify that a compilation error prevents an overall success result.
            FileHelper.SaveResultFile(TestContext, "test.bicep", "// Valid test target.", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test valid 'test.bicep' = {}
test missing 'missing.bicep' = {}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation valid (test.bicep) Passed!");
                output.Should().NotContain("All 1 evaluations passed!");
                error.Should().Contain("Error BCP091");
            }
        }

        [TestMethod]
        public async Task Test_commandNoParams_ShouldSucceed()
        {
            // Test should succeed when there are no required params
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);


            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"// test content here", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("Skipped");
            error.Should().NotContain("Failed");

            output.Should().Contain("All 1 evaluations passed!");
        }

        [TestMethod]
        public async Task Test_commandWithRequiredParam_ShouldSucceed()
        {
            // Test should succeed when passing a required parameter
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"param foo string
                                                                              output foo string = foo", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {params:{foo:'ShouldSucceed'}}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("Skipped");
            error.Should().NotContain("Failed");
            output.Should().Contain("All 1 evaluations passed!");
        }
        [TestMethod]
        public async Task Test_commandAllAssertionsPassed_ShouldSucceed()
        {
            // Test should succeed when all Assertions pass
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);


            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"param foo string
                                                                              assert isEqual = foo == 'ShouldSucceed'", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {params:{foo:'ShouldSucceed'}}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("Skipped");
            error.Should().NotContain("Failed");

            output.Should().NotBeEmpty();
            output.Should().Contain("All 1 evaluations passed!");
        }


        [TestMethod]
        public async Task Test_command_ShouldFail()
        {
            // Test should fail when not passing a required parameter
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"param foo string
                                                                                  output foo string = foo", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {foo: 1}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain("Evaluation foo (test.bicep) Skipped!");
            }

        }
        [TestMethod]
        public async Task Test_commandNotPassingAllRequiredParams_ShouldFail()
        {
            // Test should fail when not passing a required parameter
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"param foo string
                                                                              output foo string = foo", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {params:{}}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain("Evaluation foo (test.bicep) Skipped!");
            }
        }

        [TestMethod]
        public async Task Test_commandAssertionFails_ShouldFail()
        {
            // Test should fail when at least one assertion fails
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "test.bicep", @"param foo string
                                                                              assert isEqual = foo == 'ShouldNotSucceed'", outputFileDir);
            var bicepPath = FileHelper.SaveResultFile(TestContext, "main.bicep", @"test foo 'test.bicep' = {params:{foo:'ShouldSucceed'}}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", bicepPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain("Evaluation foo (test.bicep) Failed");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_RunsEveryMatchedTarget()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            var target = @"param foo string
assert isEqual = foo == 'ShouldSucceed'";

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), target, outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "two.bicep"), target, outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "_skipped.bicep"), target, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
    exclude: ['_*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation policy (modules/one.bicep) Passed!");
                output.Should().Contain("Evaluation policy (modules/two.bicep) Passed!");
                output.Should().NotContain("_skipped.bicep");
                output.Should().Contain("All 2 evaluations passed!");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_BindsEachTargetIndependently()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            // This target needs a parameter the test does not supply. It must fail on its own without
            // hiding the outcome of the target that can be evaluated.
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "two.bicep"), @"param foo string
param extra string
assert isEqual = foo == extra", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation policy (modules/one.bicep) Passed!");
                error.Should().Contain("Evaluation policy (modules/two.bicep) Skipped!");

                // An evaluation failure must never echo the parameters or template it was given.
                error.Should().NotContain("ShouldSucceed");
                error.Should().NotContain("$schema");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_MatchingNothingIsAnError()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    include: ['nothing/*.bicep']
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Evaluation policy Skipped!");
                error.Should().Contain("matched no files");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_MatchingNothingIsAllowedWhenOptedIn()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    include: ['nothing/*.bicep']
    allowEmpty: true
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                error.Should().NotContain("Skipped");
            }
        }

        [TestMethod]
        public async Task Test_Pattern_DiscoversEveryMatchingTestFile()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            FileHelper.SaveResultFile(TestContext, "alpha.biceptest", @"test policy 'target.bicep' = {
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            FileHelper.SaveResultFile(TestContext, "beta.biceptest", @"test policy 'target.bicep' = {
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--pattern", Path.Combine(outputFileDir, "*.biceptest"));

            using (new AssertionScope())
            {
                result.Should().Be(0);

                // Two files declare a test of the same name against the same target. The file must be
                // named in the result, otherwise the two outcomes are indistinguishable.
                output.Should().Contain("Evaluation alpha.biceptest: policy (target.bicep) Passed!");
                output.Should().Contain("Evaluation beta.biceptest: policy (target.bicep) Passed!");

                // One summary covers the whole run rather than one per file.
                output.Should().Contain("All 2 evaluations passed!");
                Regex.Matches(output, "evaluations passed").Should().HaveCount(1);
            }
        }

        [TestMethod]
        public async Task Test_Pattern_ReportsFilesInAStableOrder()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);

            foreach (var name in new[] { "c", "a", "b" })
            {
                FileHelper.SaveResultFile(TestContext, $"{name}.biceptest", "test policy 'target.bicep' = {}", outputFileDir);
            }

            var (output, _, result) = await Bicep(settings, "test", "--pattern", Path.Combine(outputFileDir, "*.biceptest"));

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.IndexOf("a.biceptest").Should().BeLessThan(output.IndexOf("b.biceptest"));
                output.IndexOf("b.biceptest").Should().BeLessThan(output.IndexOf("c.biceptest"));
            }
        }

        [TestMethod]
        public async Task Test_Pattern_MatchingNoFilesIsAnError()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var pattern = Path.Combine(outputFileDir, "*.biceptest");
            var (output, error, result) = await Bicep(settings, "test", "--pattern", pattern);

            using (new AssertionScope())
            {
                // An empty suite must never look like a suite that passed.
                result.Should().Be(1);
                output.Should().NotContain("passed");
                error.Should().Contain($@"The pattern ""{pattern}"" did not match any test files.");
            }
        }

        [TestMethod]
        public async Task Test_List_ReportsInventoryWithoutEvaluating()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            // This target would fail if it were evaluated. Listing must not evaluate it.
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), @"param foo string
assert isEqual = foo == 'NeverMatches'", outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "two.bicep"), @"param foo string
assert isEqual = foo == 'NeverMatches'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--list");

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("main.biceptest: policy -> modules/one.bicep");
                output.Should().Contain("main.biceptest: policy -> modules/two.bicep");

                // Listing answers "what would run". It must never claim a target compiles or passes.
                output.Should().NotContain("Passed");
                output.Should().NotContain("Failed");
                output.Should().NotContain("evaluations");
                error.Should().NotContain("Evaluation");
            }
        }

        [TestMethod]
        public async Task Test_List_ReportsTestsThatResolveToNoTargets()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    include: ['nothing/*.bicep']
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--list");

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("main.biceptest: policy -> (no targets)");
                error.Should().Contain("matched no files");
            }
        }

        [TestMethod]
        public async Task Test_JsonOutput_IsParseableAndFreeOfProgressText()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test passing 'target.bicep' = {
  params: {
    foo: 'ShouldSucceed'
  }
}
test failing 'target.bicep' = {
  params: {
    foo: 'ShouldFail'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

            using (new AssertionScope())
            {
                // The host must be able to parse the structured stream even though the run failed.
                result.Should().Be(1);

                var root = JsonDocument.Parse(output).RootElement;
                root.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
                root.GetProperty("mode").GetString().Should().Be("run");

                var cases = root.GetProperty("cases").EnumerateArray().ToArray();
                cases.Should().HaveCount(2);
                cases[0].GetProperty("caseId").GetString().Should().Be("main.biceptest#passing#target.bicep");
                cases[0].GetProperty("status").GetString().Should().Be("passed");
                cases[1].GetProperty("status").GetString().Should().Be("failed");
                cases[1].GetProperty("assertions").GetProperty("failedNames").EnumerateArray()
                    .Select(x => x.GetString()).Should().Equal("isEqual");

                root.GetProperty("summary").GetProperty("failed").GetInt32().Should().Be(1);

                // No progress text is mixed into the structured stream.
                output.Should().NotContain("Evaluation");
                output.Should().NotContain("evaluations passed");

                // Nor does the structured stream carry the parameter values the test supplied.
                output.Should().NotContain("ShouldFail");
            }
        }

        [TestMethod]
        public async Task Test_JsonOutput_ReportsInventoryForListMode()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--list", "--output-format", "json");

            using (new AssertionScope())
            {
                result.Should().Be(0);

                var root = JsonDocument.Parse(output).RootElement;
                root.GetProperty("mode").GetString().Should().Be("list");

                var single = root.GetProperty("cases").EnumerateArray().Single();
                single.GetProperty("status").GetString().Should().Be("listed");
                single.GetProperty("target").GetString().Should().Be("modules/one.bicep");

                // Listing never evaluates, so it never reports assertion outcomes.
                single.TryGetProperty("assertions", out _).Should().BeFalse();
            }
        }

        [TestMethod]
        public async Task Lint_BicepTestFile_ReportsDiagnosticsWithoutRunningTests()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            // This target's assertion is false. Linting must not evaluate it.
            FileHelper.SaveResultFile(TestContext, "target.bicep", @"param foo string
assert isEqual = foo == 'NeverMatches'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy 'target.bicep' = {
  params: {
    foo: 1
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "lint", testPath);

            using (new AssertionScope())
            {
                // A test file is ordinary Bicep source, so its own type errors are reported.
                result.Should().Be(1);
                error.Should().Contain("BCP036");

                // Linting analyses source. It never evaluates the tests.
                output.Should().NotContain("Evaluation");
                error.Should().NotContain("Evaluation");
            }
        }

        [TestMethod]
        public async Task Format_BicepTestFile_FormatsInPlace()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", "test    policy   'target.bicep'    = {  }", outputFileDir);

            var (output, error, result) = await Bicep(settings, "format", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                error.Should().BeEmpty();

                // Formatting preserves the .biceptest extension rather than emitting a .bicep file.
                File.Exists(testPath).Should().BeTrue();
                File.ReadAllText(testPath).Should().Contain("test policy 'target.bicep' = {}");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_PicksUpNewFilesWithoutEditingTheTest()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            var target = @"param foo string
assert isEqual = foo == 'ShouldSucceed'";

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), target, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (firstOutput, _, firstResult) = await Bicep(settings, "test", testPath);

            firstResult.Should().Be(0);
            firstOutput.Should().Contain("All 1 evaluations passed!");

            // A new matching file is covered by the next run. The test declaration is untouched.
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "two.bicep"), target, outputFileDir);

            var (secondOutput, _, secondResult) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                secondResult.Should().Be(0);
                secondOutput.Should().Contain("Evaluation policy (modules/two.bicep) Passed!");
                secondOutput.Should().Contain("All 2 evaluations passed!");
            }
        }

        [TestMethod]
        public async Task Test_MatchSelector_IdentitiesDoNotDependOnTheWorkingDirectory()
        {
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
}", outputFileDir);

            async Task<string> RunFrom(string currentDirectory)
            {
                var settings = new InvocationSettings(
                    new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true),
                    BicepTestConstants.ClientFactory,
                    BicepTestConstants.TemplateSpecRepositoryFactory,
                    Environment: TestEnvironment.Default with { CurrentDirectory = currentDirectory });

                var (output, _, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

                result.Should().Be(0);

                return output;
            }

            // Selection is owned by the test file, so the same invocation from a different directory
            // must produce byte-identical case identities.
            var fromRoot = await RunFrom(outputFileDir);
            var fromNested = await RunFrom(Path.Combine(outputFileDir, "modules"));

            fromNested.Should().Be(fromRoot);
            fromRoot.Should().Contain("main.biceptest#policy#modules/one.bicep");
        }

        [TestMethod]
        public async Task Test_MatchSelector_ReportsPerTargetOutcomesAndAssertionCounts()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "passing.bicep"), @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "failing.bicep"), @"param foo string
assert isEqual = foo == 'ShouldSucceed'
assert isNever = foo == 'NeverMatches'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

            using (new AssertionScope())
            {
                result.Should().Be(1);

                var cases = JsonDocument.Parse(output).RootElement.GetProperty("cases").EnumerateArray().ToArray();

                var failing = cases.Single(x => x.GetProperty("target").GetString() == "modules/failing.bicep");
                failing.GetProperty("status").GetString().Should().Be("failed");
                failing.GetProperty("assertions").GetProperty("total").GetInt32().Should().Be(2);
                failing.GetProperty("assertions").GetProperty("failed").GetInt32().Should().Be(1);

                // The passing target is unaffected by the failing one and keeps its own counts.
                var passing = cases.Single(x => x.GetProperty("target").GetString() == "modules/passing.bicep");
                passing.GetProperty("status").GetString().Should().Be("passed");
                passing.GetProperty("assertions").GetProperty("total").GetInt32().Should().Be(1);
                passing.GetProperty("assertions").GetProperty("failed").GetInt32().Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_FailOn_NamesEveryOffendingDeclaration()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "failon");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'
                }

                resource other 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'other'
                  location: 'westus'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    noSqlServers: {
                      failOn: filter(target.resources, r => r.type == 'Microsoft.Sql/servers')
                      message: 'SQL servers belong in the sql module.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Assertion noSqlServers failed!");
                error.Should().Contain("SQL servers belong in the sql module.");
                // Both violations are reported: a policy that stops at the first offender would
                // understate the work needed to make the target compliant.
                error.Should().Contain("main.bicep(1): sql");
                error.Should().Contain("main.bicep(6): other");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_FailOn_PassesWhenTheCollectionIsEmpty()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "failonpass");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource stg 'Microsoft.Storage/storageAccounts@2022-09-01' = {
                  name: 'stg'
                  location: 'westus'
                  sku: { name: 'Standard_LRS' }
                  kind: 'StorageV2'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    noSqlServers: {
                      failOn: filter(target.resources, r => r.type == 'Microsoft.Sql/servers')
                      message: 'SQL servers belong in the sql module.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation sourcePolicy (src/main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_PassWhen_JudgesTheConditionItself()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "passwhen");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource stg 'Microsoft.Storage/storageAccounts@2022-09-01' = {
                  name: 'stg'
                  location: 'westus'
                  sku: { name: 'Standard_LRS' }
                  kind: 'StorageV2'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    declaresSomething: {
                      passWhen: length(target.resources) > 0
                      message: 'Every file must declare at least one resource.'
                    }
                    declaresAModule: {
                      passWhen: length(target.modules) > 0
                      message: 'Every file must declare at least one module.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Failed at 1 / 2 assertions!");
                error.Should().Contain("Assertion declaresAModule failed!");
                error.Should().Contain("Every file must declare at least one module.");
                error.Should().NotContain("Assertion declaresSomething failed!");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_WithModules_SeesTransitivelyReachableDeclarations()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "withmodules");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src", "modules"));

            FileHelper.SaveResultFile(TestContext, "src/modules/child.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'
                }
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "src/app.bicep", """
                module child 'modules/child.bicep' = {
                  name: 'child'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['app.bicep']
                  }
                  assertions: {
                    localScopeExcludesTheChild: {
                      passWhen: length(target.resources) == 0
                      message: 'The entrypoint declares no resources of its own.'
                    }
                    composedScopeIncludesTheChild: {
                      failOn: filter(target.withModules.resources, r => r.type == 'Microsoft.Sql/servers')
                      message: 'SQL servers must not appear anywhere in the composed tree.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Failed at 1 / 2 assertions!");
                error.Should().Contain("Assertion composedScopeIncludesTheChild failed!");
                // The violation is attributed to the file that actually declares it, not to the entrypoint.
                error.Should().Contain("child.bicep(1): sql");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_CanUseTestFileVariables()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "variables");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var bannedTypes = ['Microsoft.Sql/servers']
                var unusedByAnyAssertion = 'this must not affect evaluation'

                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    noBannedTypes: {
                      failOn: filter(target.resources, r => contains(bannedTypes, r.type))
                      message: 'Banned resource types must not be declared.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Assertion noBannedTypes failed!");
                error.Should().Contain("main.bicep(1): sql");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertions_ReplaceLegacyTargetTemplateAssertions()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "replace");
            Directory.CreateDirectory(outputFileDir);

            // The target declares an assertion that would fail, and no parameters are supplied. A test
            // that brings its own source assertions must neither run nor require any of that.
            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param value int
                assert isNegative = value < 0
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    include: ['main.bicep']
                  }
                  assertions: {
                    declaresNoResources: {
                      passWhen: length(target.resources) == 0
                      message: 'The file must declare no resources.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation sourcePolicy (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertionFailures_AreReportedInTheJsonContract()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "report");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    noSqlServers: {
                      failOn: filter(target.resources, r => r.type == 'Microsoft.Sql/servers')
                      message: 'SQL servers belong in the sql module.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

            result.Should().Be(1);

            var document = JsonDocument.Parse(output).RootElement;
            var assertions = document.GetProperty("cases")[0].GetProperty("assertions");
            var failure = assertions.GetProperty("failures")[0];

            using (new AssertionScope())
            {
                failure.GetProperty("name").GetString().Should().Be("noSqlServers");
                failure.GetProperty("message").GetString().Should().Be("SQL servers belong in the sql module.");
                failure.GetProperty("violations").EnumerateArray().Select(x => x.GetString()).Should().Equal("main.bicep(1): sql");
            }
        }

        [TestMethod]
        public async Task Test_WithoutTestFrameworkEnabled_ShouldFail()        {
            var (output, error, result) = await Bicep(
                services => services.WithFeatureOverrides(new(TestFrameworkEnabled: false)),
                "test", "/dev/zero.bicep");

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().BeEmpty();

                error.Should().NotBeEmpty();
                error.Should().Contain("TestFrameWork not enabled");
            }
        }
    }
}
