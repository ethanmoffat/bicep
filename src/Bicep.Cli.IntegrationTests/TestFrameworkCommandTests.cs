// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bicep.Core;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using Bicep.IO.Abstraction;
using Bicep.IO.FileSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
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
                error.Should().Contain("The path to a .bicep or .biceptest file, or a glob pattern matching them, must be specified.");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation passing (main.bicep) Passed!");
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", bicepPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation valid (test.bicep) Passed!");
                output.Should().NotContain("Passed! - Failed:");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("could not be evaluated");
            error.Should().NotContain("Failed");

            output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("could not be evaluated");
            error.Should().NotContain("Failed");
            output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", bicepPath);

            result.Should().Be(0);
            error.Should().NotBeEmpty();
            error.Should().NotContain("could not be evaluated");
            error.Should().NotContain("Failed");

            output.Should().NotBeEmpty();
            output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");
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
                error.Should().Contain("Evaluation foo (test.bicep) could not be evaluated!");
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
                error.Should().Contain("Evaluation foo (test.bicep) could not be evaluated!");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation policy (modules/one.bicep) Passed!");
                output.Should().Contain("Evaluation policy (modules/two.bicep) Passed!");
                output.Should().NotContain("_skipped.bicep");
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 2, Total: 2,");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation policy (modules/one.bicep) Passed!");
                error.Should().Contain("Evaluation policy (modules/two.bicep) could not be evaluated!");

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
                error.Should().Contain("Evaluation policy could not be evaluated!");
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
                error.Should().NotContain("could not be evaluated");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", Path.Combine(outputFileDir, "*.biceptest"));

            using (new AssertionScope())
            {
                result.Should().Be(0);

                // Two files declare a test of the same name against the same target. The file must be
                // named in the result, otherwise the two outcomes are indistinguishable.
                output.Should().Contain("Evaluation alpha.biceptest: policy (target.bicep) Passed!");
                output.Should().Contain("Evaluation beta.biceptest: policy (target.bicep) Passed!");

                // One summary covers the whole run rather than one per file.
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 2, Total: 2,");
                Regex.Matches(output, "Passed! - Failed:").Should().HaveCount(1);
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

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "all", Path.Combine(outputFileDir, "*.biceptest"));

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
            var (output, error, result) = await Bicep(settings, "test", pattern);

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
                output.Should().NotContain("Passed! - Failed:");

                // Nor does the structured stream carry the parameter values the test supplied.
                output.Should().NotContain("ShouldFail");
            }
        }

        [TestMethod]
        public async Task Test_JsonOutput_ReportsMeasuredDurations()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), "assert alwaysTrue = true", outputFileDir);
            // Does not compile, so it errors - and rejecting it still costs time.
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "broken.bicep"), "resource nope 'Not.A/type' = {}", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
}", outputFileDir);

            var (output, _, _) = await Bicep(settings, "test", testPath, "--output-format", "json");

            using (new AssertionScope())
            {
                var root = JsonDocument.Parse(output).RootElement;
                var cases = root.GetProperty("cases").EnumerateArray().ToArray();

                cases.Should().HaveCount(2);

                // A real run must produce real measurements. Serializing the field is not enough: a
                // duration that is always zero would satisfy the shape and tell a host nothing.
                foreach (var testCase in cases)
                {
                    testCase.GetProperty("durationMs").GetDouble().Should().BePositive(
                        $"evaluating {testCase.GetProperty("target").GetString()} takes measurable time");
                }

                // Including the one that could not be evaluated.
                cases.Should().Contain(x => x.GetProperty("status").GetString() == "errored");

                var total = cases.Sum(x => x.GetProperty("durationMs").GetDouble());
                root.GetProperty("summary").GetProperty("durationMs").GetDouble()
                    .Should().BeApproximately(total, 0.01, "the summary is the sum of the cases it summarizes");
            }
        }

        [TestMethod]
        public async Task Test_JUnitOutput_ReportsSuitesCasesAndFailures()
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

            var (output, _, result) = await Bicep(settings, "test", testPath, "--output-format", "junit");

            using (new AssertionScope())
            {
                result.Should().Be(1);

                var root = XDocument.Parse(output).Root!;

                root.Name.LocalName.Should().Be("testsuites");
                root.Attribute("tests")!.Value.Should().Be("2");
                root.Attribute("failures")!.Value.Should().Be("1");

                // One suite per declaration, even when two declarations share a target.
                var suites = root.Elements("testsuite").ToArray();
                suites.Should().HaveCount(2);
                suites[0].Attribute("name")!.Value.Should().Be("main.biceptest#passing");
                suites[1].Attribute("name")!.Value.Should().Be("main.biceptest#failing");

                suites[0].Elements("testcase").Single().Element("failure").Should().BeNull();
                suites[1].Elements("testcase").Single().Element("failure")!
                    .Attribute("message")!.Value.Should().Contain("isEqual");

                // A real run must produce real measurements, in seconds.
                double.Parse(root.Attribute("time")!.Value, System.Globalization.CultureInfo.InvariantCulture)
                    .Should().BePositive();

                // No progress text is mixed into the structured stream, and it never carries the
                // parameter values the test supplied.
                output.Should().NotContain("Evaluation");
                output.Should().NotContain("ShouldFail");
            }
        }

        [TestMethod]
        public async Task Test_JUnitOutput_ReportsAnUnevaluatedTargetAsAnError()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "one.bicep"), "assert alwaysTrue = true", outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "broken.bicep"), "resource nope 'Not.A/type' = {}", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
}", outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath, "--output-format", "junit");

            using (new AssertionScope())
            {
                var root = XDocument.Parse(output).Root!;
                var cases = root.Descendants("testcase").ToArray();

                cases.Should().HaveCount(2);

                // The command reports failure, so the published document must not say otherwise.
                // JUnit's "skipped" is treated as benign by CI systems, which would let a suite whose
                // targets all failed to compile publish as green.
                result.Should().Be(1);
                root.Attribute("errors")!.Value.Should().Be("1");
                root.Attribute("skipped")!.Value.Should().Be("0");
                root.Descendants("skipped").Should().BeEmpty();

                var errored = cases.Single(x => x.Element("error") is not null);
                errored.Attribute("name")!.Value.Should().Be("modules/broken.bicep");
                errored.Element("error")!.Value.Should().NotBeEmpty();
            }
        }

        [TestMethod]
        public async Task Test_JUnitOutput_IsRejectedForListMode()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy 'target.bicep' = {}", outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--list", "--output-format", "junit");

            using (new AssertionScope())
            {
                // Listing evaluates nothing. Emitting a JUnit document of cases that never ran would
                // publish an inventory as if it were a passing test run.
                result.Should().Be(1);
                output.Should().BeEmpty();
                error.Should().Contain("--list does not produce test results");
            }
        }

        [TestMethod]
        public async Task Test_ResultsFile_WritesTheDocumentToTheFileAndKeepsTheHumanLog()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test failing 'target.bicep' = {
  params: {
    foo: 'ShouldFail'
  }
}", outputFileDir);

            // Nested under a directory that does not exist, so a caller never has to pre-create it.
            var resultsPath = Path.Combine(outputFileDir, "results", "nested", "results.xml");

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-format", "junit", "--results-file", resultsPath);

            using (new AssertionScope())
            {
                // A pipeline that only gets results from a passing run cannot report what went wrong,
                // so the file is written even though the run failed.
                result.Should().Be(1);
                File.Exists(resultsPath).Should().BeTrue();

                var root = XDocument.Parse(File.ReadAllText(resultsPath)).Root!;
                root.Name.LocalName.Should().Be("testsuites");
                root.Attribute("failures")!.Value.Should().Be("1");

                // The format says which document to produce; the results file says where to put it.
                // With the document in a file, the console carries the ordinary human log - here on
                // stderr, where failures always go - rather than nothing at all.
                error.Should().Contain("Evaluation failing");
                error.Should().Contain("Failed! - Failed:");

                // And the document is not duplicated onto the console.
                output.Should().NotContain("<testsuites");
                error.Should().NotContain("<testsuites");
            }
        }

        [TestMethod]
        public async Task Test_ResultsFile_RequiresAStatedFormat()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", "test policy 'target.bicep' = {}", outputFileDir);
            var resultsPath = Path.Combine(outputFileDir, "results.xml");

            var (_, error, result) = await Bicep(settings, "test", testPath, "--results-file", resultsPath);

            using (new AssertionScope())
            {
                // Guessing the format - from the extension, or from a default that may later change -
                // would silently write a document the pipeline cannot parse.
                result.Should().Be(1);
                error.Should().Contain("--results-file requires");
                File.Exists(resultsPath).Should().BeFalse();
            }
        }

        [TestMethod]
        public async Task Test_ResultsFile_WritesTheInventoryForListMode()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", "test policy 'target.bicep' = {}", outputFileDir);
            var resultsPath = Path.Combine(outputFileDir, "inventory.json");

            var (output, _, result) = await Bicep(settings, "test", testPath, "--list", "--output-format", "json", "--results-file", resultsPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);

                JsonDocument.Parse(File.ReadAllText(resultsPath)).RootElement
                    .GetProperty("mode").GetString().Should().Be("list");

                // The human inventory is still listed on stdout.
                output.Should().Contain("main.biceptest: policy -> target.bicep");
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

            var (firstOutput, _, firstResult) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            firstResult.Should().Be(0);
            firstOutput.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");

            // A new matching file is covered by the next run. The test declaration is untouched.
            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "two.bicep"), target, outputFileDir);

            var (secondOutput, _, secondResult) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                secondResult.Should().Be(0);
                secondOutput.Should().Contain("Evaluation policy (modules/two.bicep) Passed!");
                secondOutput.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 2, Total: 2,");
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
            //
            // Durations are excluded from the comparison: they are a measurement of this machine at
            // this moment, so requiring them to match would be asserting that two runs take exactly
            // the same time. Everything else - identities, order, statuses, assertion counts - is
            // still compared byte for byte, which is what "does not depend on the working directory"
            // actually claims.
            var fromRoot = WithoutDurations(await RunFrom(outputFileDir));
            var fromNested = WithoutDurations(await RunFrom(Path.Combine(outputFileDir, "modules")));

            fromNested.Should().Be(fromRoot);
            fromRoot.Should().Contain("main.biceptest#policy#modules/one.bicep");
        }

        /// <summary>
        /// Blanks measured durations so two runs can be compared for everything except how long they
        /// took. Asserts that it actually replaced something, so the comparison cannot silently become
        /// vacuous if the field is renamed.
        /// </summary>
        private static string WithoutDurations(string json)
        {
            var normalized = Regex.Replace(json, "\"durationMs\": [0-9.]+", "\"durationMs\": 0");

            normalized.Should().NotBe(json, "the report should carry durations to blank out");

            return normalized;
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

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

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

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

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

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

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
        public async Task Test_InputCases_CompileEachTargetOnceHoweverManyCasesRun()
        {
            // Compilation depends only on the target: a case supplies parameters to the emitted
            // template and cannot change how a target compiles. Recompiling per case would therefore
            // multiply the cost of a run by the number of cases while producing the same model.
            async Task<(int first, int second, int total)> RunWithCases(string caseDeclarations)
            {
                var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
                var outputFileDir = FileHelper.GetResultFilePath(TestContext, $"compile-once-{caseDeclarations.GetHashCode():x}");
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
                var inputPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", $"""
                    using 'policy.biceptest'

                    {caseDeclarations}
                    """, outputFileDir);

                var fileSystem = new FileSystem();
                var explorer = new CountingFileExplorer(new FileSystemFileExplorer(fileSystem));

                var (output, _, result) = await Bicep(
                    settings,
                    services => services
                        .AddSingleton<IFileSystem>(fileSystem)
                        .AddSingleton<IFileExplorer>(explorer),
                    TestContext.CancellationTokenSource.Token,
                    "test",
                    "--output-detail",
                    "all",
                    testPath,
                    "--inputs",
                    inputPath);

                result.Should().Be(0, "the run has to succeed for its reads to mean anything");

                return (explorer.GetReadCount("first.bicep"), explorer.GetReadCount("second.bicep"), Regex.Matches(output, "Passed!").Count - 1);
            }

            var single = await RunWithCases("case only = { maxResources: 5 }");
            var triple = await RunWithCases("""
                case low = { maxResources: 5 }

                case mid = { maxResources: 6 }

                case high = { maxResources: 7 }
                """);

            using (new AssertionScope())
            {
                // Guards the comparison below: two counts that are both zero would agree for the wrong
                // reason, and three times the work has to actually have been requested.
                single.first.Should().BeGreaterThan(0);
                single.total.Should().Be(2);
                triple.total.Should().Be(6);

                triple.first.Should().Be(single.first, "a target is read to compile it, and it compiles once however many cases run");
                triple.second.Should().Be(single.second, "a target is read to compile it, and it compiles once however many cases run");
            }
        }

        [TestMethod]
        public async Task Test_InputCases_NameTheCaseWhenATargetCannotBeCompiled()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "broken-target-cases");
            Directory.CreateDirectory(Path.Combine(outputFileDir, "src"));

            FileHelper.SaveResultFile(TestContext, "src/broken.bicep", "var value = noSuchSymbol", outputFileDir);
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

            var (_, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // A target that will not compile fails every case that was going to run against it, and
                // each one says which case it was: otherwise two outcomes report as the same thing and a
                // host cannot tell how many cases were lost.
                result.Should().Be(1);
                error.Should().Contain("Evaluation sizePolicy (src/broken.bicep) [policy.biceptestparam: strict] could not be evaluated!");
                error.Should().Contain("Evaluation sizePolicy (src/broken.bicep) [policy.biceptestparam: relaxed] could not be evaluated!");
                error.Should().Contain("Failed: 0, Errored: 2");
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

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // Two targets times two cases. A case never changes which targets a test applies to.
                result.Should().Be(0);
                output.Should().Contain("Evaluation sizePolicy (src/first.bicep) [policy.biceptestparam: strict] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/first.bicep) [policy.biceptestparam: relaxed] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/second.bicep) [policy.biceptestparam: strict] Passed!");
                output.Should().Contain("Evaluation sizePolicy (src/second.bicep) [policy.biceptestparam: relaxed] Passed!");
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 4, Total: 4,");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

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
        public async Task Test_InputCasesApplyToTheDiscoveredTestFileTheyNameAndNotTheOthers()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-discovered");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param capacity int = 1

                output capacity int = capacity
                """, outputFileDir);

            // One discovered test takes cases; the other takes none. A pattern covering both must
            // therefore run each with what belongs to it.
            FileHelper.SaveResultFile(TestContext, "sized.biceptest", """
                param expected int

                test sizePolicy 'main.bicep' = {
                  params: {
                    capacity: expected
                  }
                  assertions: {
                    capacityIsWhatTheCaseAsked: {
                      passWhen: target.evaluated.outputs.capacity == expected
                      message: 'The target should deploy the capacity the case supplied.'
                    }
                  }
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "shape.biceptest", """
                test shapePolicy 'main.bicep' = {
                  assertions: {
                    declaresNoResources: {
                      passWhen: length(target.resources) == 0
                      message: 'The file must declare no resources.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "sized.biceptestparam", """
                using 'sized.biceptest'

                case small = {
                  expected: 2
                }

                case large = {
                  expected: 9
                }
                """, outputFileDir);

            var pattern = Path.Combine(outputFileDir, "*.biceptest");
            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", pattern, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                error.Should().NotContain("not the test file being run");
                output.Should().Contain("Evaluation sized.biceptest: sizePolicy (main.bicep) [sized.biceptestparam: small] Passed!");
                output.Should().Contain("Evaluation sized.biceptest: sizePolicy (main.bicep) [sized.biceptestparam: large] Passed!");
                // The test that declares no inputs is run once, not once per case.
                output.Should().Contain("Evaluation shape.biceptest: shapePolicy (main.bicep) Passed!");
                output.Should().Contain("Passed: 3, Total: 3");
            }
        }

        [TestMethod]
        public async Task Test_InputCasesNamingATestFileThatIsNotRun_AreReportedRatherThanLost()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-unbound");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", outputFileDir);
            FileHelper.SaveResultFile(TestContext, "absent.biceptest", "param expected int = 0", outputFileDir);

            var runDir = Path.Combine(outputFileDir, "run");
            Directory.CreateDirectory(runDir);
            FileHelper.SaveResultFile(TestContext, "main.bicep", "param unused string = ''", runDir);

            foreach (var name in new[] { "first.biceptest", "second.biceptest" })
            {
                FileHelper.SaveResultFile(TestContext, name, """
                    test shapePolicy 'main.bicep' = {
                      assertions: {
                        declaresNoResources: {
                          passWhen: length(target.resources) == 0
                          message: 'The file must declare no resources.'
                        }
                      }
                    }
                    """, runDir);
            }

            var inputPath = FileHelper.SaveResultFile(TestContext, "absent.biceptestparam", """
                using 'absent.biceptest'

                case only = {
                  expected: 1
                }
                """, outputFileDir);

            // The pattern deliberately excludes the test the input names, so every discovered file
            // passes the input over and nothing would otherwise say the cases never ran.
            var pattern = Path.Combine(runDir, "*.biceptest");
            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", pattern, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("absent.biceptestparam: the test file this input supplies cases for was not among the files being run, so none of its cases ran.");
                output.Should().Contain("Evaluation first.biceptest: shapePolicy (main.bicep) Passed!");
                output.Should().Contain("Evaluation second.biceptest: shapePolicy (main.bicep) Passed!");
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

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

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
        public async Task Test_DeploymentContext_SuppliesAmbientValuesAndIsOverriddenPerCase()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-context");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param expectedLocation string
                param expectedResourceGroup string

                assert locationMatches = resourceGroup().location == expectedLocation
                assert resourceGroupMatches = resourceGroup().name == expectedResourceGroup
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param expectedLocation string
                param expectedResourceGroup string

                test contextPolicy 'main.bicep' = {
                  params: {
                    expectedLocation: expectedLocation
                    expectedResourceGroup: expectedResourceGroup
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  resourceGroup: 'rg-contoso'
                  resourceGroupLocation: 'westus'
                }

                case usesFileDefaults = {
                  expectedLocation: 'westus'
                  expectedResourceGroup: 'rg-contoso'
                }

                @resourceGroupLocation('westeurope')
                case overridesOneProperty = {
                  expectedLocation: 'westeurope'
                  expectedResourceGroup: 'rg-contoso'
                }

                case inheritsAfterAnOverride = {
                  expectedLocation: 'westus'
                  expectedResourceGroup: 'rg-contoso'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // File defaults reach every case, a decorator replaces only the property it names, and
                // the overriding case never mutates the defaults the following case inherits.
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: usesFileDefaults] Passed!");
                output.Should().Contain("[cases.biceptestparam: overridesOneProperty] Passed!");
                output.Should().Contain("[cases.biceptestparam: inheritsAfterAnOverride] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_DeploymentName_ComesFromContextAtTheRootAndFromTheDeclarationInModules()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-deployment-name");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "child.bicep", """
                param tag string

                output stamp string = '${deployment().name}-${tag}'
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                module child 'child.bicep' = {
                  name: '${deployment().name}-child'
                  params: {
                    tag: 'leaf'
                  }
                }

                output rootName string = deployment().name
                output childStamp string = child.outputs.stamp
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test names 'main.bicep' = {
                  params: {}
                  assertions: {
                    rootUsesTheSuppliedName: {
                      passWhen: target.evaluated.outputs.rootName == 'contoso-deploy'
                      message: 'The root deployment name should come from the case context.'
                    }
                    moduleUsesItsOwnName: {
                      passWhen: target.evaluated.outputs.childStamp == 'contoso-deploy-child-leaf'
                      message: 'A module should see the name its own declaration computed.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  deploymentName: 'contoso-deploy'
                }

                case named = {
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // The root takes the name the case supplied, and the module takes the name its own
                // declaration computed rather than reusing the root's.
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: named] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_DeploymentName_WhenNotSupplied_ReportsTheMissingContext()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-deployment-name-missing");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                assert namedDeployment = !empty(deployment().name)
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test names 'main.bicep' = {
                  params: {}
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                // No name was stated, so the failure names the missing context rather than inventing a
                // deployment name nobody chose.
                result.Should().Be(1);
                error.Should().Contain("could not be evaluated!");
                error.Should().Contain("no deployment name was supplied");
            }
        }

        [TestMethod]
        public async Task Test_DeploymentLocation_IsSuppliedToTheEvaluation()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-deployment-location");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                targetScope = 'subscription'

                output where string = deployment().location
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test located 'main.bicep' = {
                  params: {}
                  assertions: {
                    locationComesFromTheCase: {
                      passWhen: target.evaluated.outputs.where == 'westus2'
                      message: 'The deployment location should come from the case context.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  deploymentName: 'contoso-deploy'
                  deploymentLocation: 'westus2'
                }

                case located = {
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: located] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_DeploymentLocation_WhenNotSupplied_ACrossSubscriptionModuleCannotBeEvaluated()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-deployment-location-missing");
            Directory.CreateDirectory(outputFileDir);

            // Neither file mentions deployment(). Bicep emits "location": "[deployment().location]"
            // for a module deployed to another subscription, so the template needs a deployment
            // location that its author never wrote.
            FileHelper.SaveResultFile(TestContext, "child.bicep", """
                targetScope = 'subscription'

                param tag string

                output stamp string = tag
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                targetScope = 'subscription'

                param otherSubscriptionId string

                module reader 'child.bicep' = {
                  name: 'reader'
                  scope: subscription(otherSubscriptionId)
                  params: {
                    tag: 'leaf'
                  }
                }

                output stamp string = reader.outputs.stamp
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test crossSubscription 'main.bicep' = {
                  params: {
                    otherSubscriptionId: '00000000-0000-0000-0000-000000000000'
                  }
                  assertions: {
                    moduleRuns: {
                      passWhen: target.evaluated.outputs.stamp == 'leaf'
                      message: 'The cross-subscription module should be evaluated.'
                    }
                  }
                }
                """, outputFileDir);

            var withoutPath = FileHelper.SaveResultFile(TestContext, "without.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  deploymentName: 'contoso-deploy'
                }

                case unstated = {
                }
                """, outputFileDir);

            var withPath = FileHelper.SaveResultFile(TestContext, "with.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  deploymentName: 'contoso-deploy'
                  deploymentLocation: 'westus2'
                }

                case stated = {
                }
                """, outputFileDir);

            var (_, withoutError, withoutResult) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", withoutPath);
            var (withOutput, _, withResult) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", withPath);

            using (new AssertionScope())
            {
                // Without a stated location the compiler-emitted read has no answer, and the
                // evaluation says which property is missing rather than guessing a region.
                withoutResult.Should().Be(1);
                withoutError.Should().Contain("'location' doesn't exist");

                // Stating it is all that is needed; nothing in the source changes.
                withResult.Should().Be(0);
                withOutput.Should().Contain("[with.biceptestparam: stated] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_DeploymentContext_DoesNotAssignProductionParametersOfTheSameName()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-context-params");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param resourceGroupLocation string

                assert locationIsSet = !empty(resourceGroupLocation)
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test contextIsNotAParameter 'main.bicep' = {
                  params: {}
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  resourceGroupLocation: 'westus'
                }

                case onlyContext = {
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // Context is evaluation metadata, so a production parameter that happens to share its
                // name is still unsatisfied rather than silently filled in.
                result.Should().Be(1);
                error.Should().Contain("[cases.biceptestparam: onlyContext] could not be evaluated!");
                error.Should().Contain("The value for the template parameter 'resourceGroupLocation'");
            }
        }

        [TestMethod]
        public async Task Test_WithoutAnInputFile_KeepsThePlaceholderContext()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "cases-context-none");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                assert locationIsSet = !empty(resourceGroup().location)
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test contextPolicy 'main.bicep' = {}
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                // A test that states no context still evaluates, using the evaluator's own placeholders
                // rather than a context invented by the runner.
                result.Should().Be(0);
                output.Should().Contain("Evaluation contextPolicy (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_ExpandsLoopsAndExcludesFalseConditions()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-loop");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param regions string[]
                param enableBackup bool

                resource accounts 'Microsoft.Storage/storageAccounts@2023-01-01' = [for (region, index) in regions: {
                  name: 'data${index}'
                  location: region
                }]

                resource backup 'Microsoft.Storage/storageAccounts@2023-01-01' = if (enableBackup) {
                  name: 'backup'
                  location: regions[0]
                }

                resource shared 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'shared'
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test shape 'main.bicep' = {
                  params: {
                    regions: ['eastus', 'westus2', 'northeurope']
                    enableBackup: false
                  }
                  assertions: {
                    threeDeclarations: {
                      passWhen: length(target.resources) == 3
                      message: 'Source counts declarations.'
                    }
                    threeInstances: {
                      passWhen: length(target.evaluated.resources) == 3
                      message: 'One instance per region, no backup and no existing reference.'
                    }
                    namesAreResolved: {
                      passWhen: join(map(target.evaluated.resources, r => r.name), ',') == 'data0,data1,data2'
                      message: 'Each instance carries its resolved name.'
                    }
                    instancesShareOneDeclaration: {
                      passWhen: length(union(map(target.evaluated.resources, r => r.symbolicName), [])) == 1
                      message: 'All three instances come from the same declaration.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation shape (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_KeepsEachModuleCallDistinctAndResolvesItsOutputs()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-modules");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "stamp.bicep", """
                param role string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: '${role}data'
                  location: 'eastus'
                }

                output accountName string = account.name
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                module primary 'stamp.bicep' = {
                  name: 'primary'
                  params: {
                    role: 'primary'
                  }
                }

                module secondary 'stamp.bicep' = {
                  name: 'secondary'
                  params: {
                    role: 'secondary'
                  }
                }

                output primaryAccountName string = primary.outputs.accountName
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test shape 'main.bicep' = {
                  assertions: {
                    localDeclaresNothing: {
                      passWhen: empty(target.evaluated.resources)
                      message: 'The entrypoint deploys nothing of its own.'
                    }
                    sourceDeduplicatesTheModule: {
                      passWhen: length(target.withModules.resources) == 1
                      message: 'The shared module contributes its declaration once.'
                    }
                    eachCallIsItsOwnInstance: {
                      passWhen: join(map(target.evaluated.withModules.resources, r => r.name), ',') == 'primarydata,secondarydata'
                      message: 'Each module call is evaluated with its own arguments.'
                    }
                    instanceIdsAreDistinct: {
                      passWhen: length(union(map(target.evaluated.withModules.resources, r => r.instanceId), [])) == 2
                      message: 'Two calls to the same module are two instances.'
                    }
                    moduleOutputsFlowBack: {
                      passWhen: target.evaluated.outputs.primaryAccountName == 'primarydata'
                      message: 'A module output is computed offline and read by the caller.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation shape (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_GivesEachModuleLoopIterationItsOwnArguments()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-module-loop");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "stamp.bicep", """
                param stamp object
                param position string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: '${toLower(stamp.role)}data${position}'
                  location: 'eastus'
                }
                """, outputFileDir);

            // Every argument each iteration passes is written as copyIndex() into the caller's own
            // values, so nothing here resolves unless the iteration the instance belongs to is known.
            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param roles string[]

                var stamps = [for role in roles: [
                  { role: role, tier: 'hot' }
                  { role: role, tier: 'cold' }
                ]]

                module stamp 'stamp.bicep' = [for (entry, index) in flatten(stamps): {
                  name: 'stamp-${index}'
                  params: {
                    stamp: entry
                    position: '${index}${entry.tier}'
                  }
                }]
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param roles string[]

                test shape 'main.bicep' = {
                  params: {
                    roles: roles
                  }
                  assertions: {
                    everyIterationIsDeployed: {
                      passWhen: length(target.evaluated.withModules.resources) == length(roles) * 2
                      message: 'The loop deploys two stamps per role.'
                    }
                    eachIterationSeesItsOwnItem: {
                      passWhen: join(map(target.evaluated.withModules.resources, r => r.name), ',') == 'primarydata0hot,primarydata1cold,secondarydata2hot,secondarydata3cold'
                      message: 'Each iteration is evaluated with the item and index it was given.'
                    }
                    instanceIdsAreDistinct: {
                      passWhen: length(union(map(target.evaluated.withModules.resources, r => r.instanceId), [])) == length(roles) * 2
                      message: 'Each iteration of a looped module call is its own instance.'
                    }
                  }
                }
                """, outputFileDir);

            var inputsPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                case twoRoles = {
                  roles: ['primary', 'secondary']
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputsPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation shape (main.bicep) [policy.biceptestparam: twoRoles] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_ViolationsPointAtTheDeclaringModuleLine()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-violations");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "stamp.bicep", """
                resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
                  name: 'plan'
                  location: 'eastus'
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                module stamp 'stamp.bicep' = {
                  name: 'stamp'
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test storageOnly 'main.bicep' = {
                  assertions: {
                    onlyStorage: {
                      failOn: filter(target.evaluated.withModules.resources, r => !startsWith(r.type, 'Microsoft.Storage/'))
                      message: 'Only storage may be deployed.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("Only storage may be deployed.");
                error.Should().Contain("stamp.bicep(1): plan");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_IsOnlyComputedWhenAnAssertionAsksForIt()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-lazy");
            Directory.CreateDirectory(outputFileDir);

            // The target cannot be evaluated at all without a value for `required`.
            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param required string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: required
                  location: 'eastus'
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test sourcePolicy = {
                  match: {
                    include: ['main.bicep']
                  }
                  assertions: {
                    oneDeclaration: {
                      passWhen: length(target.resources) == 1
                      message: 'A source policy needs no deployment inputs.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation sourcePolicy (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_IsComputedPerInputCase()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-cases");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: '${prefix}data'
                  location: resourceGroup().location
                }

                output accountLocation string = 'eastus'
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param prefix string

                test naming 'main.bicep' = {
                  params: {
                    prefix: prefix
                  }
                  assertions: {
                    nameFollowsTheCase: {
                      passWhen: target.evaluated.resources[0].name == '${prefix}data'
                      message: 'The evaluated name follows the case that produced it.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                case contoso = {
                  prefix: 'contoso'
                }

                case fabrikam = {
                  prefix: 'fabrikam'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: contoso] Passed!");
                output.Should().Contain("[cases.biceptestparam: fabrikam] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_ResolvesChainedModuleArguments()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "chained-modules");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "first.bicep", """
                param prefix string

                output token string = '${prefix}-token'
                """, Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "second.bicep", """
                param token string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: replace(token, '-', '')
                  location: 'eastus'
                }

                output accountName string = account.name
                """, Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string

                module first 'parts/first.bicep' = {
                  name: 'first'
                  params: {
                    prefix: prefix
                  }
                }

                module second 'parts/second.bicep' = {
                  name: 'second'
                  params: {
                    token: first.outputs.token
                  }
                }

                output accountName string = second.outputs.accountName
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test chained 'main.bicep' = {
                  params: {
                    prefix: 'contoso'
                  }
                  assertions: {
                    // The second module's argument is the first module's output, so it can only be known
                    // after the first module has been evaluated.
                    argumentFlowsBetweenModules: {
                      passWhen: target.evaluated.outputs.accountName == 'contosotoken'
                      message: 'A module argument taken from another module output should be resolved.'
                    }
                    instanceIsNamedAccordingly: {
                      passWhen: target.evaluated.withModules.resources[0].name == 'contosotoken'
                      message: 'The deployed instance should carry the resolved name.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Evaluation chained (main.bicep) Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_AnswerReferenceAndListRequests()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-answer");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param identityName string

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: identityName
                }

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'contosodata'
                }

                output principalId string = identity.properties.principalId

                #disable-next-line outputs-should-not-contain-secrets
                output keyName string = account.listKeys().keys[0].keyName
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param identityName string

                var scope = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup'

                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '${scope}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/${identityName}'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                  accountKeys: {
                    operation: 'listKeys'
                    resourceId: '${scope}/providers/Microsoft.Storage/storageAccounts/contosodata'
                    apiVersion: '2023-01-01'
                    response: {
                      keys: [
                        {
                          keyName: 'key1'
                        }
                      ]
                    }
                  }
                }

                test runtimeReads 'main.bicep' = {
                  params: {
                    identityName: identityName
                  }
                  assertions: {
                    readsMockedValues: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1' && target.evaluated.outputs.keyName == 'key1'
                      message: 'The deployment should read the values the test configured.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                case contoso = {
                  identityName: 'contoso-identity'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: contoso] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_AreMatchedByTheScopeAResourceDeclares()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-declared-scope");
            Directory.CreateDirectory(outputFileDir);

            // The deployment runs at subscription scope, and each resource says where it actually lives.
            // The identity is in another resource group, and the vault is in another subscription too.
            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                targetScope = 'subscription'

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                  scope: resourceGroup('identity-rg')
                }

                resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
                  name: 'contoso-vault'
                  scope: resourceGroup('00000000-0000-0000-0000-000000000009', 'vault-rg')
                }

                output principalId string = identity.properties.principalId
                output vaultUri string = vault.properties.vaultUri
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/identity-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                  vault: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000009/resourceGroups/vault-rg/providers/Microsoft.KeyVault/vaults/contoso-vault'
                    apiVersion: '2023-07-01'
                    response: {
                      properties: {
                        vaultUri: 'https://contoso-vault.vault.azure.net/'
                      }
                    }
                  }
                }

                test scopes 'main.bicep' = {
                  params: {}
                  assertions: {
                    identityInAnotherResourceGroup: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1'
                      message: 'A mock should be matched by the resource group the declaration names.'
                    }
                    vaultInAnotherSubscription: {
                      passWhen: target.evaluated.outputs.vaultUri == 'https://contoso-vault.vault.azure.net/'
                      message: 'A mock should be matched by the subscription the declaration names.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                deploymentContext = {
                  subscriptionId: '00000000-0000-0000-0000-000000000001'
                }

                case scoped = {}
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                // Addressing either one by the deployment's own scope would name a resource that does
                // not exist, and the mock written against the real ID would never be reached.
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: scoped] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_UnsetResponseField_FailsWhenConsumed()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-unset");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "configured.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "unconfigured.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output clientId string = identity.properties.clientId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var scope = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup'

                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '${scope}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                }

                test configured 'configured.bicep' = {
                  params: {}
                  assertions: {
                    readsConfiguredField: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1'
                      message: 'A configured field is readable.'
                    }
                  }
                }

                test unconfigured 'unconfigured.bicep' = {
                  params: {}
                  assertions: {
                    readsUnconfiguredField: {
                      passWhen: target.evaluated.outputs.clientId == 'client-1'
                      message: 'An unconfigured field is unset until it is read.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                // The unset field only matters where it is consumed: the independent test still runs.
                result.Should().Be(1);
                output.Should().Contain("Evaluation configured (configured.bicep) Passed!");
                error.Should().Contain("Evaluation unconfigured (unconfigured.bicep)");
                error.Should().Contain("'clientId' doesn't exist");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_UnansweredRequest_NamesTheRequest()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-unanswered");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test unmocked 'main.bicep' = {
                  params: {}
                  assertions: {
                    readsAnUnmockedValue: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1'
                      message: 'Nothing answers this read.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("No mock answers reference on");
                error.Should().Contain("Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity");
                error.Should().Contain("(2023-01-31)");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_DuplicateSetupsAreRejected()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-duplicate");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var resourceId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'

                mocks = {
                  first: {
                    operation: 'reference'
                    resourceId: resourceId
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                  second: {
                    operation: 'reference'
                    resourceId: resourceId
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-2'
                      }
                    }
                  }
                }

                test ambiguous 'main.bicep' = {
                  params: {}
                  assertions: {
                    readsAnAmbiguousValue: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1'
                      message: 'Two setups answer the same request.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("\"first\", \"second\"");
                error.Should().Contain("answer the same request");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_ServeOrdinaryAndFullViewsOfOneResponse()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-views");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                output location string = identity.location
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      location: 'westus2'
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                }

                test bothViews 'main.bicep' = {
                  assertions: {
                    servesEachViewFromTheSameResponse: {
                      passWhen: target.evaluated.outputs.principalId == 'principal-1' && target.evaluated.outputs.location == 'westus2'
                      message: 'One response should answer both the properties view and the full envelope.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_MatchTheDeclaredApiVersionRatherThanAnyVersion()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-apiversion");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var resourceId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'

                mocks = {
                  older: {
                    operation: 'reference'
                    resourceId: resourceId
                    apiVersion: '2018-11-30'
                    response: {
                      properties: {
                        principalId: 'from-the-older-version'
                      }
                    }
                  }
                  declared: {
                    operation: 'reference'
                    resourceId: resourceId
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'from-the-declared-version'
                      }
                    }
                  }
                }

                test versioned 'main.bicep' = {
                  assertions: {
                    usesTheVersionTheSourceDeclares: {
                      passWhen: target.evaluated.outputs.principalId == 'from-the-declared-version'
                      message: 'The api version the source declares should decide which setup answers.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_DoNotMatchADifferentOperationOrRequestBody()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-nomatch");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'contosodata'
                }

                #disable-next-line outputs-should-not-contain-secrets
                output keyName string = account.listKeys().keys[0].keyName
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var resourceId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.Storage/storageAccounts/contosodata'

                mocks = {
                  wrongOperation: {
                    operation: 'reference'
                    resourceId: resourceId
                    apiVersion: '2023-01-01'
                    response: {
                      properties: {
                        keys: [
                          {
                            keyName: 'key1'
                          }
                        ]
                      }
                    }
                  }
                  wrongBody: {
                    operation: 'listKeys'
                    resourceId: resourceId
                    apiVersion: '2023-01-01'
                    requestBody: {
                      expand: 'kerb'
                    }
                    response: {
                      keys: [
                        {
                          keyName: 'key1'
                        }
                      ]
                    }
                  }
                }

                test exactMatching 'main.bicep' = {
                  assertions: {
                    readsTheKeyName: {
                      passWhen: target.evaluated.outputs.keyName == 'key1'
                      message: 'The key name should be read.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("No mock answers listKeys");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_ThatAreNeverUsedAreNotAFailure()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-unused");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                resource unread 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'contosodata'
                }

                output identityId string = identity.id
                output unreadId string = unread.id
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                mocks = {
                  neverRequested: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'principal-1'
                      }
                    }
                  }
                }

                test unusedSetups 'main.bicep' = {
                  assertions: {
                    resolvesIdsWithoutAnyRuntimeRead: {
                      passWhen: endsWith(target.evaluated.outputs.identityId, '/contoso-identity') && endsWith(target.evaluated.outputs.unreadId, '/contosodata')
                      message: 'Resource IDs are computed from source, so neither a used nor an unused setup is required.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_AreBuiltFromEachCasesOwnInputs()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-per-case");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output principalId string = identity.properties.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param principalId string

                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: principalId
                      }
                    }
                  }
                }

                test parameterized 'main.bicep' = {
                  assertions: {
                    readsThisCasesValue: {
                      passWhen: target.evaluated.outputs.principalId == principalId
                      message: 'Each case should read the value its own inputs configured.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                case first = {
                  principalId: 'principal-from-the-first-case'
                }

                case second = {
                  principalId: 'principal-from-the-second-case'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: first] Passed!");
                output.Should().Contain("[cases.biceptestparam: second] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_AnswerEachModuleWithItsOwnResponse()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-two-modules");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, "reader.bicep", """
                param identityName string

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: identityName
                }

                output principalId string = identity.properties.principalId
                """, Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                module csi 'modules/reader.bicep' = {
                  name: 'csi'
                  params: {
                    identityName: 'csi-identity'
                  }
                }

                module app 'modules/reader.bicep' = {
                  name: 'app'
                  params: {
                    identityName: 'app-identity'
                  }
                }

                output csiPrincipalId string = csi.outputs.principalId
                output appPrincipalId string = app.outputs.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var scope = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities'

                mocks = {
                  csiIdentity: {
                    operation: 'reference'
                    resourceId: '${scope}/csi-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'csi-principal'
                      }
                    }
                  }
                  appIdentity: {
                    operation: 'reference'
                    resourceId: '${scope}/app-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        principalId: 'app-principal'
                      }
                    }
                  }
                }

                test twoIdentities 'main.bicep' = {
                  assertions: {
                    eachModuleReadsItsOwnIdentity: {
                      passWhen: target.evaluated.outputs.csiPrincipalId == 'csi-principal' && target.evaluated.outputs.appPrincipalId == 'app-principal'
                      message: 'Two calls of one module should each read the identity they addressed.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_MissingFieldFailsOnlyTheCaseThatOmitsIt()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-case-isolation");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output clientId string = identity.properties.clientId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param response object

                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: response
                    }
                  }
                }

                test readsClientId 'main.bicep' = {
                  assertions: {
                    clientIdIsAvailable: {
                      passWhen: target.evaluated.outputs.clientId == 'client-1'
                      message: 'The client ID should be the one the test configured.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                case incomplete = {
                  response: {
                    principalId: 'principal-1'
                  }
                }

                case complete = {
                  response: {
                    principalId: 'principal-1'
                    clientId: 'client-1'
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("[cases.biceptestparam: incomplete]");
                error.Should().Contain("'clientId' doesn't exist");
                output.Should().Contain("[cases.biceptestparam: complete] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_DoNotRequireFieldsOnABranchThatIsNotTaken()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-short-circuit");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param useIdentity bool

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                output value string = useIdentity ? identity.properties.principalId : 'none'
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param useIdentity bool

                mocks = {
                  identity: {
                    operation: 'reference'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-identity'
                    apiVersion: '2023-01-31'
                    response: {
                      properties: {
                        tenantId: 'tenant-1'
                      }
                    }
                  }
                }

                test branch 'main.bicep' = {
                  params: {
                    useIdentity: useIdentity
                  }
                  assertions: {
                    unreadFieldIsNotRequired: {
                      passWhen: target.evaluated.outputs.value == 'none'
                      message: 'A field on a branch that is not taken should not be required, and should not become empty.'
                    }
                  }
                }
                """, outputFileDir);

            var inputPath = FileHelper.SaveResultFile(TestContext, "cases.biceptestparam", """
                using 'policy.biceptest'

                case off = {
                  useIdentity: false
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("[cases.biceptestparam: off] Passed!");
            }
        }

        [TestMethod]
        public async Task Test_Mocks_ResponseDataIsNotEchoedWhenEvaluationFails()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "mocks-no-echo");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'contosodata'
                }

                #disable-next-line outputs-should-not-contain-secrets
                output secondKey string = account.listKeys().keys[1].value
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                mocks = {
                  accountKeys: {
                    operation: 'listKeys'
                    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/DummyResourceGroup/providers/Microsoft.Storage/storageAccounts/contosodata'
                    apiVersion: '2023-01-01'
                    response: {
                      keys: [
                        {
                          keyName: 'key1'
                          value: 'synthetic-key-material-do-not-print'
                        }
                      ]
                    }
                  }
                }

                test readsAKeyThatIsNotThere 'main.bicep' = {
                  assertions: {
                    keyIsRead: {
                      passWhen: target.evaluated.outputs.secondKey != ''
                      message: 'The second key should be readable.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                error.Should().Contain("out of bounds");
                output.Should().NotContain("synthetic-key-material-do-not-print");
                error.Should().NotContain("synthetic-key-material-do-not-print");
            }
        }

        [TestMethod]
        public async Task Test_WithoutTestFrameworkEnabled_ShouldFail()
        {
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

        /// <summary>
        /// Three targets with three different outcomes, which every reporting test below runs against:
        /// one passes, one breaks the policy, and one cannot be evaluated at all because it declares a
        /// parameter the test does not supply.
        /// </summary>
        private string SaveMixedOutcomeTestFile(string outputFileDir)
        {
            Directory.CreateDirectory(Path.Combine(outputFileDir, "modules"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "passes.bicep"), @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "fails.bicep"), @"param foo string
assert isEqual = foo == 'SomethingElse'", outputFileDir);

            FileHelper.SaveResultFile(TestContext, Path.Combine("modules", "errors.bicep"), @"param foo string
param unsupplied string
assert isEqual = foo == unsupplied", outputFileDir);

            return FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);
        }

        [TestMethod]
        public async Task Test_Summary_CountsFailedAndErroredSeparately()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = SaveMixedOutcomeTestFile(outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);

                // A broken policy and a policy that never ran call for different action, so they are
                // counted apart - but neither is reported as anything other than a failure of the run.
                error.Should().Contain("Failed! - Failed: 1, Errored: 1, Passed: 1, Total: 3,");
                error.Should().NotContain("Skipped");
            }
        }

        [TestMethod]
        public async Task Test_Summary_ReportsThePassingRunInTheSameShape()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", @"param foo string
assert isEqual = foo == 'ShouldSucceed'", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "main.biceptest", @"test policy 'main.bicep' = {
  params: {
    foo: 'ShouldSucceed'
  }
}", outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(0);

                // One shape whatever the outcome, so neither a reader nor a log scraper has to
                // recognize two different summaries.
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 1, Total: 1,");
                output.Should().MatchRegex(@"Duration: \d");
            }
        }

        [TestMethod]
        public async Task Test_OutputDetail_DefaultsToReportingOnlyWhatWentWrong()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = SaveMixedOutcomeTestFile(outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);

                // A run over many targets is read to find out what is wrong with it. Confirmation that
                // nothing is wrong with the rest buries that.
                output.Should().NotContain("modules/passes.bicep");
                output.Should().NotContain("Passed!");

                error.Should().Contain("modules/fails.bicep");
                error.Should().Contain("modules/errors.bicep");
                error.Should().Contain("Failed! - Failed: 1, Errored: 1, Passed: 1, Total: 3,");
            }
        }

        [TestMethod]
        public async Task Test_OutputDetail_All_ReportsEveryCase()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = SaveMixedOutcomeTestFile(outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation policy (modules/passes.bicep) Passed!");
                error.Should().Contain("Evaluation policy (modules/fails.bicep) Failed");
                error.Should().Contain("Evaluation policy (modules/errors.bicep) could not be evaluated!");
                error.Should().Contain("Failed! - Failed: 1, Errored: 1, Passed: 1, Total: 3,");
            }
        }

        [TestMethod]
        public async Task Test_OutputDetail_Summary_ReportsTheCountsAndNothingElse()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = SaveMixedOutcomeTestFile(outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "summary", testPath);

            using (new AssertionScope())
            {
                // Quieter output never changes the verdict: the exit status still carries it.
                result.Should().Be(1);

                output.Should().NotContain("modules/");
                error.Should().NotContain("modules/");
                error.Should().Contain("Failed! - Failed: 1, Errored: 1, Passed: 1, Total: 3,");
            }
        }

        [TestMethod]
        public async Task Test_OutputDetail_DoesNotFilterTheMachineReadableDocument()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            var testPath = SaveMixedOutcomeTestFile(outputFileDir);

            var (output, _, result) = await Bicep(settings, "test", "--output-detail", "summary", "--output-format", "json", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);

                // The console detail level is a reading aid. A host that received a filtered document
                // could not tell a case that passed from one that was never reported.
                var document = JsonDocument.Parse(output);
                var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

                cases.Should().HaveCount(3);
                cases.Select(x => x.GetProperty("status").GetString())
                    .Should().BeEquivalentTo(["passed", "failed", "errored"]);
            }
        }

        [TestMethod]
        public async Task Test_InputArgument_AcceptsAGlobInPlaceOfAFilePath()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "policy"));

            FileHelper.SaveResultFile(TestContext, Path.Combine("policy", "target.bicep"), "assert alwaysTrue = true", outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("policy", "one.biceptest"), "test policy 'target.bicep' = {}", outputFileDir);
            FileHelper.SaveResultFile(TestContext, Path.Combine("policy", "two.biceptest"), "test policy 'target.bicep' = {}", outputFileDir);

            // The same argument that names one file matches many. A caller who had to know in advance
            // which of two spellings their path needed got a file-not-found when they guessed wrong.
            var (output, _, result) = await Bicep(settings, "test", Path.Combine(outputFileDir, "**", "*.biceptest").Replace('\\', '/'));

            using (new AssertionScope())
            {
                result.Should().Be(0);
                output.Should().Contain("Passed! - Failed: 0, Errored: 0, Passed: 2, Total: 2,");
            }
        }

        [TestMethod]
        public async Task Test_InputArgument_RejectsTheRemovedPatternOption()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true, AssertsEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputdir");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "target.bicep", "assert alwaysTrue = true", outputFileDir);
            FileHelper.SaveResultFile(TestContext, "main.biceptest", "test policy 'target.bicep' = {}", outputFileDir);

            var (_, error, result) = await Bicep(settings, "test", "--pattern", Path.Combine(outputFileDir, "*.biceptest"));

            using (new AssertionScope())
            {
                // Silently accepting it would leave two spellings of one thing in the surface area.
                result.Should().Be(1);
                error.Should().Contain("--pattern");
            }
        }
    }
}
