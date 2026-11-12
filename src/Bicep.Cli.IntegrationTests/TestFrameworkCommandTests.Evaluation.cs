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
        public async Task Test_Evaluated_ResolvesALoopedModuleArgumentThatReadsAnotherModuleOutput()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "looped-module-argument");
            Directory.CreateDirectory(outputFileDir);
            Directory.CreateDirectory(Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "principal.bicep", """
                output id string = 'principal-1'
                """, Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "containers.bicep", """
                type container = { name: string, principals: string[] }

                param containers container[]

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for c in containers: {
                  name: c.name
                  location: 'westus2'
                  tags: { principal: c.principals[0] }
                }]
                """, Path.Combine(outputFileDir, "parts"));

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param names string[]

                module principal 'parts/principal.bicep' = {
                  name: 'principal'
                }

                module containers 'parts/containers.bicep' = {
                  name: 'containers'
                  params: {
                    containers: [for n in names: { name: n, principals: [principal.outputs.id] }]
                  }
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test looped 'main.bicep' = {
                  params: {
                    names: ['x', 'y']
                  }
                  assertions: {
                    // Each element of the loop reads the other module's output, so the argument is only
                    // known once that module has been evaluated.
                    everyContainerIsDeployed: {
                      passWhen: map(target.evaluated.withModules.resources, r => r.name) == ['x', 'y']
                      message: 'Every container built by the loop should be deployed.'
                    }
                    everyContainerReadsTheOutput: {
                      passWhen: map(target.evaluated.withModules.resources, r => r.tags.principal) == ['principal-1', 'principal-1']
                      message: 'Every container should carry the principal the other module returned.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                output.Should().Contain("Evaluation looped (main.bicep) Passed!");
                error.Should().NotContain("Expected a value of type 'Object'");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_IndexAccessReadsTheSameMembersAsPropertyAccess()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "evaluated-index-access");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param name string

                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: name
                  location: 'westus2'
                  kind: 'StorageV2'
                  sku: { name: 'Standard_LRS' }
                }

                output accountName string = account.name
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                param member string = 'evaluated'

                var evaluatedKey = 'evaluated'

                test indexed 'main.bicep' = {
                  params: {
                    name: 'contosodata'
                  }
                  assertions: {
                    evaluatedByIndex: {
                      passWhen: target['evaluated'].resources[0].name == 'contosodata'
                      message: 'target[\'evaluated\'] should read the same values as target.evaluated.'
                    }
                    outputsByIndex: {
                      passWhen: target['evaluated']['outputs']['accountName'] == 'contosodata'
                      message: 'Outputs should be readable by index.'
                    }
                    withModulesByIndex: {
                      passWhen: length(target['evaluated']['withModules']['resources']) == 1
                      message: 'withModules should be readable by index.'
                    }
                    evaluatedByVariable: {
                      passWhen: target[evaluatedKey].resources[0].name == 'contosodata'
                      message: 'A key held in a variable should read the same member.'
                    }
                    evaluatedByComputedKey: {
                      passWhen: target[member].resources[0].name == 'contosodata'
                      message: 'A key only known when the case runs should read the same member.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                output.Should().Contain("Evaluation indexed (main.bicep) Passed!");
                error.Should().NotContain("doesn't exist");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_ReportsEachOutputThatDoesNotReadAnUnansweredValue()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputs-per-output");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param name string

                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
                  name: 'contoso-identity'
                }

                resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
                  name: guid(name)
                  properties: {
                    principalId: identity.properties.principalId
                    roleDefinitionId: 'reader'
                  }
                }

                output accountName string = '${name}data'
                output principalId string = identity.properties.principalId
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test instances 'main.bicep' = {
                  params: { name: 'contoso' }
                  assertions: {
                    oneAssignment: {
                      passWhen: length(target.evaluated.resources) == 1
                      message: 'Instances do not depend on an output or a body.'
                    }
                  }
                }

                test computed 'main.bicep' = {
                  params: { name: 'contoso' }
                  assertions: {
                    accountName: {
                      passWhen: target.evaluated.outputs.accountName == 'contosodata' && target.evaluated.outputs['accountName'] == 'contosodata'
                      message: 'An output that reads nothing unanswered is reported.'
                    }
                  }
                }

                test unanswered 'main.bicep' = {
                  params: { name: 'contoso' }
                  assertions: {
                    principal: {
                      passWhen: !empty(target.evaluated.outputs.principalId)
                      message: 'An output that reads an unanswered value is named.'
                    }
                  }
                }

                test whole 'main.bicep' = {
                  params: { name: 'contoso' }
                  assertions: {
                    everyOutput: {
                      passWhen: length(items(target.evaluated.outputs)) == 2
                      message: 'Reading every output reads the one that could not be computed.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation instances (main.bicep) Passed!");
                output.Should().Contain("Evaluation computed (main.bicep) Passed!");
                error.Should().Contain("Evaluation unanswered (main.bicep) Failed at 1 / 1 assertions!");
                error.Should().Contain("Evaluation whole (main.bicep) Failed at 1 / 1 assertions!");
                error.Should().Contain("Could not be evaluated: The output 'principalId' could not be evaluated for this case: No mock answers reference on");
                error.Should().NotContain("'accountName' could not be evaluated");
            }
        }

        [TestMethod]
        public async Task Test_Evaluated_AnOutputThatReadsAnUncomputedModuleOutputNamesIt()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "outputs-module-chain");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "storage.bicep", """
                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
                  name: 'contosodata'
                }

                output name string = account.name
                output endpoint string = account.properties.primaryEndpoints.blob
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                module storage 'storage.bicep' = {
                  name: 'storage'
                }

                output name string = storage.outputs.name
                output endpoint string = storage.outputs.endpoint
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test name 'main.bicep' = {
                  params: {}
                  assertions: {
                    name: {
                      passWhen: target.evaluated.outputs.name == 'contosodata'
                      message: 'A module output that was computed flows back.'
                    }
                  }
                }

                test endpoint 'main.bicep' = {
                  params: {}
                  assertions: {
                    endpoint: {
                      passWhen: !empty(target.evaluated.outputs.endpoint)
                      message: 'The module output it passes on could not be computed.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                result.Should().Be(1);
                output.Should().Contain("Evaluation name (main.bicep) Passed!");
                error.Should().Contain("Could not be evaluated: The output 'endpoint' could not be evaluated for this case: It reads the output 'endpoint' of module 'storage', which could not be evaluated: No mock answers reference on");
            }
        }
    }
}
