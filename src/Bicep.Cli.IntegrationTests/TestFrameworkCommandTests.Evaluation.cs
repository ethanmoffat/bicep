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
    }
}
