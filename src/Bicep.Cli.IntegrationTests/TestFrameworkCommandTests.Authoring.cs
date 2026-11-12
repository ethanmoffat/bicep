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

        [TestMethod]
        public async Task Test_UserDefinedFunctions_CanBeCalledFromAssertionsParamsAndEachOther()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "user-functions");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string

                output name string = '${prefix}stg'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                type Naming = {
                  prefix: string
                  suffix: string?
                }

                func render(naming Naming) string => '${naming.prefix}${naming.?suffix ?? 'stg'}'
                func renderAll(namings Naming[]) string[] => map(namings, n => render(n))
                func isLower(value string) bool => value == toLower(value)

                param naming Naming = { prefix: 'contoso' }
                param fallback string = render({ prefix: 'fabrikam' })

                var expected = render(naming)

                test functions 'main.bicep' = {
                  params: {
                    prefix: toLower(naming.prefix)
                  }
                  assertions: {
                    fromAssertion: {
                      passWhen: target.evaluated.outputs.name == render(naming)
                      message: 'Expected ${render(naming)}.'
                    }
                    fromVariable: {
                      passWhen: target.evaluated.outputs.name == expected
                      message: 'Expected ${expected}.'
                    }
                    fromAnotherFunction: {
                      passWhen: length(filter(renderAll([naming, { prefix: 'UPPER' }]), n => !isLower(n))) == 1
                      message: 'Only the upper-case name is not lower case.'
                    }
                    fromDefault: {
                      passWhen: fallback == 'fabrikamstg'
                      message: 'The default is rendered.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().NotContain("is not found");
                output.Should().Contain("[✓] Evaluation functions (main.bicep) Passed!");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_ForExpressionVariables_EvaluateInTestAndInputFiles()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "for-variables");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param names string[]

                output joined string = join(names, '-')
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param names string[]
                param expectedJoined string

                var expected = [for (n, i) in names: { name: n, position: i + 1 }]
                var described = [for e in expected: '${e.position}:${e.name}']

                test loops 'main.bicep' = {
                  params: {
                    names: names
                  }
                  assertions: {
                    joined: {
                      passWhen: target.evaluated.outputs.joined == expectedJoined && join(map(expected, e => e.name), '-') == expectedJoined
                      message: 'Joined ${target.evaluated.outputs.joined}.'
                    }
                    described: {
                      passWhen: length(described) == 3 && described[1] == '2:${names[1]}'
                      message: 'Described ${join(described, ',')}.'
                    }
                  }
                }
                """, outputFileDir);
            var inputsPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                var items = ['a', 'b', 'c']
                var plain = [for n in items: toUpper(n)]
                var indexed = [for (n, i) in items: '${n}${i}']
                var objects = [for (n, i) in items: { name: n, index: i }]

                case plainCase = {
                  names: plain
                  expectedJoined: 'A-B-C'
                }

                case indexedCase = {
                  names: indexed
                  expectedJoined: 'a0-b1-c2'
                }

                case objectCase = {
                  names: map(objects, o => '${o.name}${o.index}')
                  expectedJoined: 'a0-b1-c2'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputsPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().NotContain("ForLoopExpression");
                output.Should().Contain("Evaluation loops (main.bicep) - 3/3 cases passed");
                result.Should().Be(0);
            }
        }
    }
}
