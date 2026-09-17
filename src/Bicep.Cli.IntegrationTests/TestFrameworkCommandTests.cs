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
        public async Task Test_SemanticAssertion_CanScopeAPolicyToADirectoryWithoutMatchingLookalikes()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "directory");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src", "sql"));
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src", "sqlbackup"));

            // Inside the approved directory, including a nested child resource.
            FileHelper.SaveResultFile(TestContext, "src/sql/approved.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'

                  resource db 'databases' = {
                    name: 'db'
                    location: 'westus'
                  }
                }
                """, outputFileDir);
            // A directory whose name merely starts with the approved one must not be treated as approved.
            FileHelper.SaveResultFile(TestContext, "src/sqlbackup/lookalike.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'lookalike'
                  location: 'westus'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sqlLocationPolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    sqlOnlyUnderTheApprovedDirectory: {
                      failOn: filter(target.resources, r => startsWith(r.type, 'Microsoft.Sql/') && !startsWith(r.file, 'sql/'))
                      message: 'SQL resources may only be declared under sql/.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                // The approved file passes even though it declares a nested SQL child.
                output.Should().Contain("Evaluation sqlLocationPolicy (src/sql/approved.bicep) Passed!");
                error.Should().Contain("Evaluation sqlLocationPolicy (src/sqlbackup/lookalike.bicep) Failed");
                error.Should().Contain("sqlbackup/lookalike.bicep(1): sql");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_DistinguishesExistingReferencesFromDeclarations()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "existing");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            // An existing reference adopts a resource rather than creating one, and text that merely
            // looks like a declaration is a comment, not a declaration.
            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                resource adopted 'Microsoft.Sql/servers@2021-11-01' existing = {
                  name: 'alreadyThere'
                }

                resource adoptedMultiline 'Microsoft.Sql/servers@2021-11-01' existing = {
                  name: 'alsoAlreadyThere'
                }

                // resource commentedOut 'Microsoft.Sql/servers@2021-11-01' = {
                //   name: 'notReal'
                // }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test creationPolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    createsNoSqlServers: {
                      failOn: filter(target.resources, r => r.type == 'Microsoft.Sql/servers' && !r.existing)
                      message: 'SQL servers must not be created here.'
                    }
                    adoptsTwo: {
                      passWhen: length(filter(target.resources, r => r.existing)) == 2
                      message: 'Both existing references must be visible as facts.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation creationPolicy (src/main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_SeesModuleAndImportRelationshipsSeparately()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "relationships");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src", "internal"));

            FileHelper.SaveResultFile(TestContext, "src/internal/helpers.bicep", """
                @export()
                func first(value string) string => toLower(value)

                @export()
                func second(value string) string => toUpper(value)
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "src/internal/shared.bicep", """
                param unused string = ''
                output echoed string = unused
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "src/consumer.bicep", """
                import { first, second } from 'internal/helpers.bicep'
                import * as everything from 'internal/helpers.bicep'

                module shared 'internal/shared.bicep' = {
                  name: 'shared'
                }

                output name string = first(second('value')) == '' ? everything.first('x') : 'y'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test dependencyPolicy = {
                  match: {
                    root: 'src'
                    include: ['consumer.bicep']
                  }
                  assertions: {
                    oneFactPerImportSite: {
                      // Two import statements, regardless of how many symbols each brings in.
                      passWhen: length(target.imports) == 2
                      message: 'Each import statement must be represented exactly once.'
                    }
                    noInternalDependencies: {
                      failOn: union(
                        filter(target.imports, i => startsWith(i.resolvedFile, 'internal/')),
                        filter(target.modules, m => startsWith(m.resolvedFile, 'internal/')))
                      message: 'Files under internal/ must not be referenced from outside.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Failed at 1 / 2 assertions!");
                error.Should().Contain("Assertion noInternalDependencies failed!");
                // Both relationship kinds are detected, each at its own site.
                error.Should().Contain("consumer.bicep(1): internal/helpers.bicep");
                error.Should().Contain("consumer.bicep(2): internal/helpers.bicep");
                error.Should().Contain("consumer.bicep(4): shared");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_InspectsConditionalAndLoopedDeclarationsWithoutAnyInputs()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "conditional");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            // Required parameters, a runtime function and declarations hidden behind a condition and a
            // loop. A source policy must still see them without any of that being resolved.
            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                param deploySql bool
                param names array

                resource conditional 'Microsoft.Sql/servers@2021-11-01' = if (deploySql) {
                  name: 'conditional'
                  location: resourceGroup().location
                }

                resource looped 'Microsoft.Sql/servers@2021-11-01' = [for name in names: {
                  name: name
                  location: resourceGroup().location
                }]
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
                      message: 'SQL servers must not be declared.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                // One fact per declaration: the condition is not evaluated and the loop is not expanded.
                error.Should().Contain("main.bicep(4): conditional");
                error.Should().Contain("main.bicep(9): looped");
                error.Should().NotContain("parameter");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertion_ThatCannotBeEvaluated_FailsRatherThanPassing()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "unevaluable");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/main.bicep", """
                output value string = 'nothing declared here'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    root: 'src'
                    include: ['**/*.bicep']
                  }
                  assertions: {
                    indexesAnEmptyCollection: {
                      passWhen: target.resources[0].type == 'Microsoft.Sql/servers'
                      message: 'This assertion cannot be evaluated against a file with no resources.'
                    }
                    genuinelyEmptyQuery: {
                      failOn: filter(target.resources, r => r.type == 'Microsoft.Sql/servers')
                      message: 'An empty result here is a real pass, not a missing answer.'
                    }
                  }
                }
                """, outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Failed at 1 / 2 assertions!");
                error.Should().Contain("Assertion indexesAnEmptyCollection failed!");
                error.Should().Contain("Could not be evaluated: The language expression property array index '0' is out of bounds.");
                // A valid query with no results is still a pass, and the broken assertion beside it
                // did not prevent it from being judged.
                error.Should().NotContain("Assertion genuinelyEmptyQuery failed!");
            }
        }

        [TestMethod]
        public async Task Test_SemanticAssertions_ReportMixedOutcomesInOneMachineReadableDocument()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mixed");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/clean.bicep", """
                output value string = 'clean'
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "src/dirty.bicep", """
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

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

            result.Should().Be(1);

            var document = JsonDocument.Parse(output).RootElement;
            var cases = document.GetProperty("cases").EnumerateArray().ToList();

            using (new AssertionScope())
            {
                // The machine-readable stream carries only the document; progress text is never mixed in.
                error.Should().NotContain("\"version\"");
                output.Should().StartWith("{");
                cases.Should().HaveCount(2);
                cases[0].GetProperty("target").GetString().Should().Be("src/clean.bicep");
                cases[0].GetProperty("status").GetString().Should().Be("passed");
                cases[0].GetProperty("assertions").GetProperty("failures").GetArrayLength().Should().Be(0);

                cases[1].GetProperty("target").GetString().Should().Be("src/dirty.bicep");
                cases[1].GetProperty("status").GetString().Should().Be("failed");
                cases[1].GetProperty("assertions").GetProperty("failures")[0]
                    .GetProperty("violations").EnumerateArray().Select(x => x.GetString())
                    .Should().Equal("dirty.bicep(1): sql");

                document.GetProperty("summary").GetProperty("passed").GetInt32().Should().Be(1);
                document.GetProperty("summary").GetProperty("failed").GetInt32().Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Test_InputCases_RunEveryCaseAgainstEveryTarget()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-matrix");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/first.bicep", "param unused string = ''", outputFileDir);
            FileHelper.SaveResultFile(TestContext, "src/second.bicep", "param unused string = ''", outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param maxResources int

                test sizePolicy = {
                  match: {
                    root: 'src'
                    include: ['*.bicep']
                  }
                  assertions: {
                    boundedResourceCount: {
                      passWhen: length(target.resources) <= maxResources
                      message: 'At most ${maxResources} resources.'
                    }
                  }
                }
                """, outputFileDir);
            var inputPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                case strict = {
                  maxResources: 0
                }

                case relaxed = {
                  maxResources: 5
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // Two targets times two cases. A case never changes which targets a test applies to.
                result.Should().Be(0);
                output.Should().Contain("Evaluation sizePolicy (src/first.bicep) [policy.biceptestparam: strict] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/first.bicep) [policy.biceptestparam: relaxed] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/second.bicep) [policy.biceptestparam: strict] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/second.bicep) [policy.biceptestparam: relaxed] Passed!");
                output.Should().Contain("All 4 evaluations passed!");
            }
        }

        [TestMethod]
        public async Task Test_InputCases_SupplyTheProductionParametersTheTestMapsIn()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-params");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param namePrefix string
                var accountName = toLower('${namePrefix}stg')
                assert nameWithinLengthLimit = length(accountName) <= 24
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "naming.biceptest", """
                param namePrefix string

                test namingRules 'main.bicep' = {
                  params: {
                    namePrefix: namePrefix
                  }
                }
                """, outputFileDir);
            var inputPath = FileHelper.SaveResultFile(TestContext, "naming.biceptestparam", """
                using 'naming.biceptest'

                case withinLimit = {
                  namePrefix: 'contoso'
                }

                case tooLong = {
                  namePrefix: 'contosoabcdefghijklmnopqrstuvwxyz'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // The case values reach the target through the test's own typed inputs, so the target's
                // own assertion is what decides each outcome.
                result.Should().Be(1);
                output.Should().Contain("Evaluation namingRules (main.bicep) [naming.biceptestparam: withinLimit] Passed!");
                error.Should().Contain("Evaluation namingRules (main.bicep) [naming.biceptestparam: tooLong] Failed");
                error.Should().Contain("Assertion nameWithinLengthLimit failed!");
            }
        }

        [TestMethod]
        public async Task Test_InputCases_AreReportedIndividuallyInTheJsonContract()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-json");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' = {
                  name: 'sql'
                  location: 'westus'
                }
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param maxResources int

                test sizePolicy = {
                  match: {
                    include: ['main.bicep']
                  }
                  assertions: {
                    boundedResourceCount: {
                      passWhen: length(target.resources) <= maxResources
                      message: 'At most ${maxResources} resources.'
                    }
                  }
                }
                """, outputFileDir);
            var inputPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                case strict = {
                  maxResources: 0
                }

                case relaxed = {
                  maxResources: 5
                }
                """, outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath, "--output-format", "json");

            var document = JsonDocument.Parse(output).RootElement;
            var cases = document.GetProperty("cases").EnumerateArray().ToArray();

            using (new AssertionScope())
            {
                result.Should().Be(1);
                cases.Should().HaveCount(2);

                // The identity distinguishes two runs of the same test and target that differ only in
                // the values they ran with, and the message reflects the values it actually judged.
                cases[0].GetProperty("caseId").GetString().Should().Be("policy.biceptest#sizePolicy#main.bicep#policy.biceptestparam#strict");
                cases[0].GetProperty("inputFile").GetString().Should().Be("policy.biceptestparam");
                cases[0].GetProperty("inputCase").GetString().Should().Be("strict");
                cases[0].GetProperty("status").GetString().Should().Be("failed");
                cases[0].GetProperty("assertions").GetProperty("failures")[0].GetProperty("message").GetString().Should().Be("At most 0 resources.");

                cases[1].GetProperty("inputCase").GetString().Should().Be("relaxed");
                cases[1].GetProperty("status").GetString().Should().Be("passed");
            }
        }

        [TestMethod]
        public async Task Test_WithoutInputCases_KeepsTheExistingReportShape()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-absent");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sizePolicy = {
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

            var (output, _, result) = await Bicep(settings, "test", testPath, "--output-format", "json");

            var document = JsonDocument.Parse(output).RootElement;
            var single = document.GetProperty("cases").EnumerateArray().Single();

            using (new AssertionScope())
            {
                result.Should().Be(0);
                single.GetProperty("caseId").GetString().Should().Be("policy.biceptest#sizePolicy#main.bicep");
                single.GetProperty("inputFile").ValueKind.Should().Be(JsonValueKind.Null);
                single.GetProperty("inputCase").ValueKind.Should().Be(JsonValueKind.Null);
            }
        }

        [TestMethod]
        public async Task Test_InputCasesBoundToAnotherTestFile_AreReportedWithoutStoppingTheRun()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-mismatch");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", outputFileDir);
            FileHelper.SaveResultFile(TestContext, "other.biceptest", "param maxResources int = 0", outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sizePolicy = {
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
            var inputPath = FileHelper.SaveResultFile(TestContext, "other.biceptestparam", """
                using 'other.biceptest'

                case strict = {
                  maxResources: 0
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // The mismatch is attributed and folded into the exit code, but the tests that can still
                // run do run.
                result.Should().Be(1);
                error.Should().Contain("other.biceptestparam: The input file supplies cases for \"other.biceptest\", not the test file being run.");
                output.Should().Contain("Evaluation sizePolicy (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_InputFileWithWrongExtension_IsRejected()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-extension");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sizePolicy = {
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
            var inputPath = FileHelper.SaveResultFile(TestContext, "values.bicepparam", "using none", outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("was not recognized as a Bicep test parameters file");
            }
        }

        [TestMethod]
        public async Task Test_MissingInputValue_SkipsOnlyTheAffectedEvaluation()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-missing");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param maxResources int

                test needsInput = {
                  match: {
                    include: ['main.bicep']
                  }
                  assertions: {
                    boundedResourceCount: {
                      passWhen: length(target.resources) <= maxResources
                      message: 'Too many resources.'
                    }
                  }
                }

                test needsNothing = {
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

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                // An input with no value fails its own assertion and says what is missing; the test that
                // needs no values still runs.
                result.Should().Be(1);
                error.Should().Contain("The input \"maxResources\" has no value. Supply it from a test case or give it a default.");
                output.Should().Contain("Evaluation needsNothing (main.bicep) Passed!");
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
