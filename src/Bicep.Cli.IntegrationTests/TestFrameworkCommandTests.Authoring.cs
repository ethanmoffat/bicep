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

        [TestMethod]
        public async Task Test_NullableInputs_WithoutAValue_EvaluateToNull()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "nullable-inputs");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string
                param suffix string?

                output name string = '${prefix}${suffix ?? '-none'}'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                type Tier = 'basic' | 'premium'

                param prefix string
                param suffix string?
                param tags object?
                param tier Tier?
                param expected string

                test nullable 'main.bicep' = {
                  params: {
                    prefix: prefix
                    suffix: suffix
                  }
                  assertions: {
                    omittedAreNull: {
                      passWhen: tags == null && tier == null && (tags.?owner ?? 'nobody') == 'nobody'
                      message: 'Tags are ${tags ?? 'null'} and tier is ${tier ?? 'null'}.'
                    }
                    nullReachesTheTarget: {
                      passWhen: target.evaluated.outputs.name == expected
                      message: 'Expected ${expected} but the name is ${target.evaluated.outputs.name}.'
                    }
                  }
                }
                """, outputFileDir);
            var inputsPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                case omitted = {
                  prefix: 'contoso'
                  expected: 'contoso-none'
                }

                case explicitNull = {
                  prefix: 'contoso'
                  suffix: null
                  tags: null
                  expected: 'contoso-none'
                }

                case supplied = {
                  prefix: 'contoso'
                  suffix: '-prod'
                  expected: 'contoso-prod'
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputsPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().NotContain("has no value");
                output.Should().Contain("Evaluation nullable (main.bicep) - 3/3 cases passed");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_RequiredInput_WithoutAValue_StillReportsThatItHasNoValue()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "required-input");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                output name string = 'stg'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param optional string?
                param required string

                test requiredInput 'main.bicep' = {
                  assertions: {
                    optionalIsNull: {
                      passWhen: optional == null
                      message: 'The optional input is null.'
                    }
                    requiredIsSet: {
                      passWhen: required == 'set'
                      message: 'The required input is set.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().Contain("[✗] Evaluation requiredInput (main.bicep) Failed at 1 / 2 assertions!");
                error.Should().Contain("Could not be evaluated: The input \"required\" has no value. Supply it from a test case or give it a default.");
                error.Should().NotContain("\"optional\"");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Test_Imports_FromATemplate_WorkInTestAndInputFiles()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "imports");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param prefix string

                output name string = '${prefix}stg'
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "settings.json", """
                { "prefix": "contoso" }
                """, outputFileDir);
            FileHelper.SaveResultFile(TestContext, "shared.bicep", """
                @export()
                var defaultPrefix = loadJsonContent('settings.json').prefix

                @export()
                var regions = [for region in ['eastus', 'westus2']: toUpper(region)]

                @export()
                type Naming = {
                  prefix: string
                }

                @export()
                func render(naming Naming) string => '${naming.prefix}stg'
                """, outputFileDir);
            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                import { defaultPrefix, Naming, render } from 'shared.bicep'
                import * as shared from 'shared.bicep'

                param naming Naming

                test imports 'main.bicep' = {
                  params: {
                    prefix: naming.prefix == defaultPrefix ? defaultPrefix : 'wrong'
                  }
                  assertions: {
                    named: {
                      passWhen: target.evaluated.outputs.name == render(naming)
                      message: 'Expected ${render(naming)}.'
                    }
                    wildcard: {
                      passWhen: target.evaluated.outputs.name == shared.render({ prefix: shared.defaultPrefix }) && shared.regions[1] == 'WESTUS2'
                      message: 'Expected the shared rendering of ${shared.defaultPrefix}.'
                    }
                  }
                }
                """, outputFileDir);
            var inputsPath = FileHelper.SaveResultFile(TestContext, "policy.biceptestparam", """
                using 'policy.biceptest'

                import { defaultPrefix, regions } from 'shared.bicep'
                import * as shared from 'shared.bicep'

                case imported = {
                  naming: { prefix: defaultPrefix }
                }

                case wildcard = {
                  naming: { prefix: substring(shared.render({ prefix: shared.defaultPrefix }), 0, length(defaultPrefix)) }
                }

                case loop = {
                  naming: { prefix: regions[0] == 'EASTUS' ? defaultPrefix : 'wrong' }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", testPath, "--inputs", inputsPath, "--output-detail", "all");

            using (new AssertionScope())
            {
                error.Should().NotContain("BCP376");
                error.Should().NotContain("BCP464");
                output.Should().Contain("Evaluation imports (main.bicep) - 3/3 cases passed");
                result.Should().Be(0);
            }
        }
    }
}
