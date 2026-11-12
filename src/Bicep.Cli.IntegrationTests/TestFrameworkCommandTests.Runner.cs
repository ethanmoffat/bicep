// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.IntegrationTests
{
    /// <summary>
    /// How the runner turns test files and input cases into results, and how it reports the ones that
    /// could not run.
    /// </summary>
    public partial class TestFrameworkCommandTests
    {
        private InvocationSettings TestFrameworkSettings => new(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);

        /// <summary>
        /// A target with one resource and a test whose single assertion bounds its resource count by
        /// a case value. Returns the path of the test file.
        /// </summary>
        private string WriteCountPolicy(string outputFileDir)
        {
            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource sql 'Microsoft.Sql/servers@2021-11-01' existing = {
                  name: 'server'
                }
                """, outputFileDir);

            return FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param maxResources int

                test sizePolicy 'main.bicep' = {
                  assertions: {
                    bounded: {
                      passWhen: length(target.resources) <= maxResources
                      message: 'At most ${maxResources} resources.'
                    }
                  }
                }
                """, outputFileDir);
        }

        private static JsonElement[] ParseCases(string output)
            => [.. JsonDocument.Parse(output).RootElement.GetProperty("cases").EnumerateArray()];

        [TestMethod]
        public async Task Test_InputCases_OneCaseThatCannotBeEvaluatedDoesNotSetAsideTheOthers()
        {
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "runner-case-error");
            Directory.CreateDirectory(outputFileDir);

            var testPath = WriteCountPolicy(outputFileDir);
            var inputPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                case first = {
                  maxResources: 1
                }

                case broken = {
                  maxResources: int('abc')
                }

                case third = {
                  maxResources: 0
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(TestFrameworkSettings, "test", testPath, "--inputs", inputPath, "--output-format", "json");
            var cases = ParseCases(output);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                cases.Select(x => x.GetProperty("inputCase").GetString()).Should().Equal("first", "broken", "third");
                cases.Select(x => x.GetProperty("status").GetString()).Should().Equal("passed", "errored", "failed");
                cases[1].GetProperty("error").GetString().Should().StartWith("The case \"broken\" could not be evaluated: The template language function 'int' cannot convert");
                error.Should().Contain("policy.biceptestparam: The case \"broken\" could not be evaluated:");
            }
        }
    }
}
