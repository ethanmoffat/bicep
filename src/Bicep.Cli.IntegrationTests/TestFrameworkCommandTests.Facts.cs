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

                module sibling 'definition.bicep' = {
                  name: 'sibling'
                  scope: managementGroup('mg-other')
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "definition.bicep", """
                targetScope = 'managementGroup'

                resource definition 'Microsoft.Authorization/policyDefinitions@2023-04-01' = {
                  name: 'definition'
                  properties: {}
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
                        ['sibling/definition', '/providers/Microsoft.Management/managementGroups/mg-other']
                      ]
                      message: 'Each instance should report what it is deployed into.'
                    }
                    everyModuleCallReportsWhereItRuns: {
                      passWhen: map(target.evaluated.modules, m => [m.name, m.scope, m.id]) == [
                        ['landingZone', '/subscriptions/22222222-2222-2222-2222-222222222222', '/subscriptions/22222222-2222-2222-2222-222222222222/providers/Microsoft.Resources/deployments/landingZone']
                        ['root', '/', '/providers/Microsoft.Resources/deployments/root']
                        ['sibling', '/providers/Microsoft.Management/managementGroups/mg-other', '/providers/Microsoft.Management/managementGroups/mg-other/providers/Microsoft.Resources/deployments/sibling']
                      ]
                      message: 'Each module deployment should report where it runs.'
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

        [TestMethod]
        public async Task Test_Facts_ReportTheDeploymentEachModuleCallCreates()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "facts-evaluated-modules");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                param stamp string
                param zones string[]
                param withAudit bool = false

                module privateZones 'zone.bicep' = [for zone in zones: {
                  name: '${stamp}-pdz-${replace(zone, '.', '-')}'
                  params: { zoneName: zone }
                }]

                module shared 'zone.bicep' = {
                  name: '${stamp}-shared'
                  scope: resourceGroup('rg-shared')
                  params: { zoneName: 'shared.example.com' }
                }

                module audit 'zone.bicep' = if (withAudit) {
                  name: '${stamp}-audit'
                  params: { zoneName: 'audit.example.com' }
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "zone.bicep", """
                param zoneName string

                resource zone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
                  name: zoneName
                  location: 'global'
                }

                module records 'records.bicep' = {
                  name: '${deployment().name}-records'
                  params: { zoneName: zone.name }
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "records.bicep", """
                param zoneName string

                output zone string = zoneName
                """, outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "naming.biceptest", """
                param stamp string
                param zones string[]

                test naming 'main.bicep' = {
                  params: {
                    stamp: stamp
                    zones: zones
                  }
                  assertions: {
                    eachCallIsNamedForItsStamp: {
                      passWhen: sort(map(target.evaluated.modules, m => [m.instanceId, m.name, m.resourceGroup]), (a, b) => a[0] < b[0]) == [
                        ['privateZones[0]', 'west-pdz-privatelink-blob-core-windows-net', 'rg-app']
                        ['privateZones[1]', 'west-pdz-privatelink-vaultcore-azure-net', 'rg-app']
                        ['shared', 'west-shared', 'rg-shared']
                      ]
                      message: 'Each module deployment is named for the stamp and the zone it creates.'
                    }
                    deploymentsAreAddressedWhereTheyRun: {
                      passWhen: map(filter(target.evaluated.modules, m => m.symbolicName == 'shared'), m => [m.id, m.scope]) == [['/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-shared/providers/Microsoft.Resources/deployments/west-shared', '/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-shared']]
                      message: 'The shared zone deployment runs in the shared resource group.'
                    }
                    callsCarryTheirDeclaration: {
                      passWhen: map(filter(target.evaluated.modules, m => m.symbolicName == 'privateZones'), m => '${m.file}:${m.line}') == ['main.bicep:5', 'main.bicep:5']
                      message: 'A call points at its declaration.'
                    }
                    nestedCallsAreNamedPerCall: {
                      passWhen: sort(map(filter(target.evaluated.withModules.modules, m => m.symbolicName == 'records'), m => [m.instanceId, m.name, m.file]), (a, b) => a[0] < b[0]) == [
                        ['privateZones[0]/records', 'west-pdz-privatelink-blob-core-windows-net-records', 'zone.bicep']
                        ['privateZones[1]/records', 'west-pdz-privatelink-vaultcore-azure-net-records', 'zone.bicep']
                        ['shared/records', 'west-shared-records', 'zone.bicep']
                      ]
                      message: 'A nested call is named from the deployment that makes it.'
                    }
                    namesFitTheDeploymentLimit: {
                      failOn: filter(target.evaluated.withModules.modules, m => length(m.name) > 64)
                      message: 'Deployment names are limited to 64 characters.'
                    }
                  }
                }
                """, outputFileDir);

            var inputsPath = FileHelper.SaveResultFile(TestContext, "naming.biceptestparam", """
                using 'naming.biceptest'

                deploymentContext = {
                  subscriptionId: '11111111-1111-1111-1111-111111111111'
                  resourceGroup: 'rg-app'
                  deploymentName: 'naming'
                  deploymentLocation: 'westus'
                }

                case west = {
                  stamp: 'west'
                  zones: ['privatelink.blob.core.windows.net', 'privatelink.vaultcore.azure.net']
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath, "--inputs", inputsPath);

            using (new AssertionScope())
            {
                error.Should().NotContain("Error");
                output.Should().Contain("Evaluation naming (main.bicep) [naming.biceptestparam: west] Passed!");
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Test_Facts_NameAModuleCallWhoseArgumentsAreOnlyKnownAtDeployment()
        {
            var settings = new InvocationSettings(new(TestContext, TestFrameworkEnabled: true), BicepTestConstants.ClientFactory, BicepTestConstants.TemplateSpecRepositoryFactory);
            var outputFileDir = FileHelper.GetResultFilePath(TestContext, "facts-evaluated-unresolved-module");
            Directory.CreateDirectory(outputFileDir);

            FileHelper.SaveResultFile(TestContext, "main.bicep", """
                resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
                  name: 'workload'
                  location: 'eastus'
                }

                module grant 'grant.bicep' = {
                  name: 'grant-${identity.name}'
                  params: {
                    principalId: identity.properties.principalId
                  }
                }
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "grant.bicep", """
                param principalId string

                module inner 'inner.bicep' = {
                  name: 'inner'
                }

                output principal string = principalId
                """, outputFileDir);

            FileHelper.SaveResultFile(TestContext, "inner.bicep", "", outputFileDir);

            var testPath = FileHelper.SaveResultFile(TestContext, "policy.biceptest", """
                test named 'main.bicep' = {
                  assertions: {
                    theCallIsNamed: {
                      passWhen: map(target.evaluated.modules, m => m.name) == ['grant-workload']
                      message: 'A deployment name does not depend on the arguments.'
                    }
                  }
                }

                test nested 'main.bicep' = {
                  assertions: {
                    readsWhatTheModuleCalls: {
                      passWhen: length(target.evaluated.withModules.modules) > 0
                      message: 'What a module calls needs the arguments it is called with.'
                    }
                  }
                }
                """, outputFileDir);

            var (output, error, result) = await Bicep(settings, "test", "--output-detail", "all", testPath);

            using (new AssertionScope())
            {
                output.Should().Contain("Evaluation named (main.bicep) Passed!");
                error.Should().Contain("The arguments of module 'grant' could not be evaluated for this case");
                result.Should().Be(1);
            }
        }
    }
}
