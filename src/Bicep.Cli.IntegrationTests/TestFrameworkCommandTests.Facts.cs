// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.IntegrationTests
{
    public partial class TestFrameworkCommandTests
    {
        [TestMethod]
        public async Task Test_Facts_ReportTheVariablesAFileDeclares()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "facts-variables");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "child.bicep", """
                param value string
                var inner = toUpper(value)
                output upper string = inner
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param location string

                var zoneName = '${location}.example.com'

                @export()
                var sharedSuffix = 'contoso'

                module child 'child.bicep' = {
                  name: 'child'
                  params: { value: zoneName }
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                var removed = ['cnameRecords', 'privateLinkCnameRecords']

                test variables 'main.bicep' = {
                  assertions: {
                    declaresItsVariablesInOrder: {
                      passWhen: map(target.variables, v => v.name) == ['zoneName', 'sharedSuffix']
                      message: 'The file declares its two variables.'
                    }
                    removedVariablesStayRemoved: {
                      failOn: filter(target.variables, v => contains(removed, v.name))
                      message: 'The legacy alias variables must stay removed.'
                    }
                    onlyTheSuffixIsExported: {
                      passWhen: map(filter(target.variables, v => v.exported), v => v.name) == ['sharedSuffix']
                      message: 'Only the suffix is shared with other files.'
                    }
                    variablesCarryTheirDeclaration: {
                      passWhen: target.variables[1].file == 'main.bicep' && target.variables[1].line == 5
                      message: 'A decorated variable starts at its first decorator.'
                    }
                    modulesContributeTheirVariables: {
                      passWhen: map(target.withModules.variables, v => '${v.file}:${v.name}') == ['main.bicep:zoneName', 'main.bicep:sharedSuffix', 'child.bicep:inner']
                      message: 'withModules adds the variables of every reachable module.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                error.Should().NotContain("Error");
                output.Should().Contain("Evaluation variables (main.bicep) Passed!");
                result.Should().Be(0);
            }
        }
    }
}
