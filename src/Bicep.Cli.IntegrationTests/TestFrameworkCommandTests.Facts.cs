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

        [TestMethod]
        public async Task Test_Facts_ReportTheConstraintsAParameterDeclares()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "facts-parameter-constraints");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                @minLength(3)
                @maxLength(24)
                type accountName = string

                @allowed(['Standard_LRS', 'Standard_ZRS'])
                param sku string = 'Standard_ZRS'

                param name accountName = 'contoso'

                @minValue(7)
                @maxValue(90)
                param retentionDays int?

                param kind 'StorageV2' | 'BlobStorage' = 'StorageV2'
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test constraints 'main.bicep' = {
                  assertions: {
                    skuIsZoneRedundantOrLocal: {
                      passWhen: filter(target.parameters, p => p.name == 'sku')[0].allowed == ['Standard_LRS', 'Standard_ZRS']
                      message: 'The SKU list must stay limited to LRS and ZRS.'
                    }
                    noGeoRedundantSku: {
                      failOn: filter(target.parameters, p => contains(p.allowed ?? [], 'Standard_GRS'))
                      message: 'Geo-redundant storage must not be offered.'
                    }
                    nameBoundsComeFromTheAlias: {
                      passWhen: filter(target.parameters, p => p.name == 'name')[0].minLength == 3 && filter(target.parameters, p => p.name == 'name')[0].maxLength == 24
                      message: 'Account names are 3 to 24 characters.'
                    }
                    retentionIsBounded: {
                      passWhen: filter(target.parameters, p => p.name == 'retentionDays')[0].minValue == 7 && filter(target.parameters, p => p.name == 'retentionDays')[0].maxValue == 90
                      message: 'Retention is 7 to 90 days.'
                    }
                    unboundedFactsAreNull: {
                      passWhen: filter(target.parameters, p => p.name == 'sku')[0].minLength == null && filter(target.parameters, p => p.name == 'kind')[0].allowed == null
                      message: 'Only declared bounds and decorator lists are reported.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                error.Should().NotContain("Error");
                output.Should().Contain("Evaluation constraints (main.bicep) Passed!");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_Facts_ReportWhatEachEvaluatedInstanceIsDeployedInto()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "facts-evaluated-scope");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                targetScope = 'managementGroup'

                resource definition 'Microsoft.Authorization/policyDefinitions@2023-04-01' = {
                  name: 'definition'
                  properties: {}
                }

                module landingZone 'subscription.bicep' = {
                  name: 'landingZone'
                  scope: subscription('22222222-2222-2222-2222-222222222222')
                }

                module root 'tenant.bicep' = {
                  name: 'root'
                  scope: tenant()
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "tenant.bicep", """
                targetScope = 'tenant'

                resource group 'Microsoft.Management/managementGroups@2023-04-01' = {
                  name: 'mg-child'
                  properties: {}
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "subscription.bicep", """
                targetScope = 'subscription'

                resource workload 'Microsoft.Resources/resourceGroups@2024-03-01' = {
                  name: 'rg-workload'
                  location: 'westus'
                }

                resource readers 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
                  name: '00000000-0000-0000-0000-00000000000a'
                  properties: {
                    principalId: 'p'
                    roleDefinitionId: 'r'
                  }
                }

                module storage 'storage.bicep' = {
                  name: 'storage'
                  scope: workload
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "storage.bicep", """
                resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
                  name: 'account'
                  location: 'westus'
                  kind: 'StorageV2'
                  sku: {
                    name: 'Standard_LRS'
                  }
                }

                resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
                  parent: account
                  name: 'default'
                }

                resource doNotDelete 'Microsoft.Authorization/locks@2020-05-01' = {
                  name: 'doNotDelete'
                  scope: account
                  properties: {
                    level: 'CanNotDelete'
                  }
                }
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "placement.biceptest", """
                var account = '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-workload/providers/Microsoft.Storage/storageAccounts/account'

                test placement 'main.bicep' = {
                  assertions: {
                    everyInstanceReportsWhatItIsDeployedInto: {
                      passWhen: map(target.evaluated.withModules.resources, r => [r.instanceId, r.scope]) == [
                        ['definition', '/providers/Microsoft.Management/managementGroups/mg-root']
                        ['landingZone/workload', '/subscriptions/22222222-2222-2222-2222-222222222222']
                        ['landingZone/readers', '/subscriptions/22222222-2222-2222-2222-222222222222']
                        ['landingZone/storage/account', '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-workload']
                        ['landingZone/storage/blobs', '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-workload']
                        ['landingZone/storage/doNotDelete', account]
                        ['root/group', '/']
                      ]
                      message: 'Each instance should report what it is deployed into.'
                    }
                    everyIdBeginsWithItsScope: {
                      failOn: filter(target.evaluated.withModules.resources, r => !startsWith(r.id, r.scope))
                      message: 'An instance is addressed beneath what it is deployed into.'
                    }
                    locksSitOnTheAccount: {
                      failOn: filter(target.evaluated.withModules.resources, r => r.type == 'Microsoft.Authorization/locks' && !endsWith(r.scope, '/storageAccounts/account'))
                      message: 'Locks belong on the storage account.'
                    }
                  }
                }
                """, outputFileDir);

            var inputsPath = FileHelper.SaveResultFile(TestContext, "placement.biceptestparam", """
                using 'placement.biceptest'

                deploymentContext = {
                  managementGroup: 'mg-root'
                  deploymentName: 'placement'
                  deploymentLocation: 'westus'
                }

                case only = {}
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputsPath);

            using (new AssertionScope())
            {
                error.Should().NotContain("Error");
                output.Should().Contain("Evaluation placement (main.bicep) [placement.biceptestparam: only] Passed!");
                result.Should().Be(0);
            }
        }
    }
}
