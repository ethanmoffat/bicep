// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.Diagnostics;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using Bicep.Testing.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.IntegrationTests
{
    [TestClass]
    public class TestParamsFileTests
    {
        private ServiceBuilder ServicesWithTestFramework => new ServiceBuilder()
            .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true));

        [NotNull]
        public TestContext? TestContext { get; set; }

        private CompilationHelper.CompilationResult CompileTestParams(params (string fileName, string fileContents)[] files)
        {
            var fileSet = new MockFileSystemTestFileSet();
            foreach (var (fileName, fileContents) in files)
            {
                fileSet.AddFile(fileName, fileContents);
            }

            return CompilationHelper.Compile(ServicesWithTestFramework, fileSet, fileSet.GetUri("cases.biceptestparam"));
        }

        private static (string, string) TestFile(string contents) => ("sample.biceptest", contents);

        private const string DefaultTestFile = """
            param location string
            param retentionDays int = 7

            test policy 'app.bicep' = {
              params: {
                location: location
              }
            }
            """;

        private const string DefaultTargetFile = """
            param location string
            output location string = location
            """;

        [TestMethod]
        public void Case_values_are_checked_against_the_inputs_the_test_declares()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    case westus = {
                      location: 'westus'
                      retentionDays: 30
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void A_case_cannot_assign_an_input_the_test_does_not_declare()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    case westus = {
                      location: 'westus'
                      regionCode: 'wus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP037", DiagnosticLevel.Error, "The property \"regionCode\" is not allowed on objects of type \"TestCase\". Permissible properties include \"retentionDays\"."),
            });
        }

        [TestMethod]
        public void A_case_must_supply_every_required_input()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    case westus = {
                      retentionDays: 30
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"case\" declaration is missing the following required properties: \"location\"."),
            });
        }

        [TestMethod]
        public void A_case_value_must_match_the_declared_input_type()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    case westus = {
                      location: 'westus'
                      retentionDays: 'thirty'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP036", DiagnosticLevel.Error, "The property \"retentionDays\" expected a value of type \"int\" but the provided value is of type \"'thirty'\"."),
            });
        }

        [TestMethod]
        public void Cases_can_share_values_through_variables()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    var defaultRetention = 30

                    case westus = {
                      location: 'westus'
                      retentionDays: defaultRetention
                    }

                    case eastus = {
                      location: 'eastus'
                      retentionDays: defaultRetention
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void Duplicate_case_names_are_reported()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    case westus = {
                      location: 'westus'
                    }

                    case westus = {
                      location: 'westus2'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP028", DiagnosticLevel.Error, "Identifier \"westus\" is declared multiple times. Remove or rename the duplicates."),
                ("BCP028", DiagnosticLevel.Error, "Identifier \"westus\" is declared multiple times. Remove or rename the duplicates."),
            });
        }

        [TestMethod]
        public void An_input_file_must_declare_at_least_one_case()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP467", DiagnosticLevel.Error, "A \".biceptestparam\" file must declare at least one \"case\"."),
            });
        }

        [TestMethod]
        public void Using_must_reference_a_test_file()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'app.bicep'

                    case westus = {
                      location: 'westus'
                    }
                    """),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP466", DiagnosticLevel.Error, "The \"using\" declaration of a \".biceptestparam\" file must reference a \".biceptest\" file."),
            });
        }

        [TestMethod]
        public void A_missing_using_is_reported_once()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    case westus = {
                      location: 'westus'
                    }
                    """));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP261", DiagnosticLevel.Error, "A using declaration must be present in this parameters file."),
            });
        }

        [TestMethod]
        public void Deployment_context_properties_are_typed()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      subscriptionId: '00000000-0000-0000-0000-000000000000'
                      resourceGroup: 'rg-contoso'
                      resourceGroupLocation: 'westus'
                    }

                    case westus = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void An_unknown_deployment_context_property_is_reported()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      region: 'westus'
                    }

                    case westus = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP037", DiagnosticLevel.Error, "The property \"region\" is not allowed on objects of type \"DeploymentContext\". Permissible properties include \"deploymentLocation\", \"deploymentName\", \"environment\", \"managementGroup\", \"resourceGroup\", \"resourceGroupLocation\", \"subscriptionId\", \"tenantId\"."),
            });
        }

        [TestMethod]
        public void A_deployment_name_can_be_supplied_and_overridden()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      deploymentName: 'contoso-deploy'
                    }

                    case usesFileDefault = {
                      location: 'westus'
                    }

                    @deploymentName('contoso-deploy-2')
                    case overridesTheName = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void An_environment_can_be_supplied_and_replaced_by_a_case()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      environment: {
                        name: 'AzureCloud'
                        suffixes: {
                          storage: 'core.windows.net'
                        }
                      }
                    }

                    case usesFileDefault = {
                      location: 'westus'
                    }

                    @environment({
                      name: 'AzureChinaCloud'
                    })
                    case replacesTheEnvironment = {
                      location: 'chinaeast2'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void An_environment_is_checked_against_the_shape_environment_returns()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      environment: {
                        suffixes: {
                          storge: 'core.windows.net'
                        }
                      }
                    }

                    @environment('AzureCloud')
                    case westus = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP089", DiagnosticLevel.Error, "The property \"storge\" is not allowed on objects of type \"suffixesProperties\". Did you mean \"storage\"?"),
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"'AzureCloud'\" is not assignable to parameter of type \"environment\"."),
            });
        }

        [TestMethod]
        public void A_case_can_override_individual_deployment_context_properties()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    deploymentContext = {
                      resourceGroup: 'rg-contoso'
                      resourceGroupLocation: 'westus'
                    }

                    @resourceGroupLocation('westeurope')
                    @subscriptionId('00000000-0000-0000-0000-000000000000')
                    case westeurope = {
                      location: 'westeurope'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void A_case_cannot_override_the_same_context_property_twice()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    @resourceGroupLocation('westus')
                    @resourceGroupLocation('westeurope')
                    case westus = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP166", DiagnosticLevel.Error, "Duplicate \"resourceGroupLocation\" decorator."),
                ("BCP166", DiagnosticLevel.Error, "Duplicate \"resourceGroupLocation\" decorator."),
            });
        }

        [TestMethod]
        public void A_case_context_override_must_be_a_string()
        {
            var result = CompileTestParams(
                ("cases.biceptestparam", """
                    using 'sample.biceptest'

                    @resourceGroupLocation(42)
                    case westus = {
                      location: 'westus'
                    }
                    """),
                TestFile(DefaultTestFile),
                ("app.bicep", DefaultTargetFile));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"42\" is not assignable to parameter of type \"string\"."),
            });
        }

        [TestMethod]
        public void Context_decorators_are_not_available_outside_input_files()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, """
                @resourceGroupLocation('westus')
                param location string = 'westus'
                """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP057", DiagnosticLevel.Error, "The name \"resourceGroupLocation\" does not exist in the current context."),
            });
        }
    }
}
