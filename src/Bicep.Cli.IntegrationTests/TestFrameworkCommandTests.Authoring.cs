// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.IntegrationTests
{
    /// <summary>
    /// What an author can write in a test file or an input file: the same expressions a template or a
    /// parameters file accepts.
    /// </summary>
    public partial class TestFrameworkCommandTests
    {
        [TestMethod]
        public async Task Test_LoadFunctions_ReadFilesBesideTheTestAndInputFiles()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "load-functions");
            var casesDir = Path.Combine(outputFileDir, "cases");
            Directory.CreateDirectory(casesDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string
                param tags object

                output name string = '${prefix}stg'
                output tagCount int = length(tags)
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "settings.json", """
                { "prefix": "contoso", "tags": { "a": "1", "b": "2" } }
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "limits.yaml", """
                maxTags: 2
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "greeting.txt", "hello", outputFileDir);
            FileHelper.SaveResultFile(TestContext, "expected.json", """
                { "tagCount": 2 }
                """, casesDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param expectedTagCount int

                test loads 'main.bicep' = {
                  params: {
                    prefix: loadJsonContent('settings.json').prefix
                    tags: loadJsonContent('settings.json', '.tags')
                  }
                  assertions: {
                    json: {
                      passWhen: target.evaluated.outputs.name == '${loadJsonContent('settings.json').prefix}stg'
                      message: 'The name is built from the prefix in settings.json.'
                    }
                    yaml: {
                      passWhen: target.evaluated.outputs.tagCount <= loadYamlContent('limits.yaml').maxTags
                      message: 'At most ${loadYamlContent('limits.yaml').maxTags} tags.'
                    }
                    text: {
                      passWhen: loadTextContent('greeting.txt') == 'hello' && loadFileAsBase64('greeting.txt') == base64('hello')
                      message: 'The text loads.'
                    }
                    fromCase: {
                      passWhen: target.evaluated.outputs.tagCount == expectedTagCount
                      message: 'The case expects ${expectedTagCount} tags.'
                    }
                  }
                }
                """, outputFileDir);
            var inputsPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using '../policy.biceptest'

                case fromJson = {
                  expectedTagCount: loadJsonContent('expected.json').tagCount
                }
                """, casesDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputsPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().NotContain("could not be evaluated");
                output.Should().Contain("[✓] Evaluation loads (main.bicep) [policy.biceptestparam: fromJson] Passed!");
                result.Should().Be(0);
            }
        }
    }
}
