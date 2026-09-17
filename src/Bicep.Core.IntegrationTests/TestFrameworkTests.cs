// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.Diagnostics;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.IntegrationTests
{
    [TestClass]
    public class TestFrameworkTests
    {
        private ServiceBuilder ServicesWithTestFramework => new ServiceBuilder()
            .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true));

        [NotNull]
        public TestContext? TestContext { get; set; }

        [TestMethod]
        public void TestFramework_is_disabled_unless_feature_is_enabled()
        {
            var result = CompilationHelper.Compile(@"
test test1 '' = {}
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP348", DiagnosticLevel.Error, "Using a test declaration statement requires enabling EXPERIMENTAL feature \"TestFramework\"."),
                ("BCP050", DiagnosticLevel.Error, @"The specified path is empty."),
            });
            result = CompilationHelper.Compile(@"
test test1 '' =
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP348", DiagnosticLevel.Error, "Using a test declaration statement requires enabling EXPERIMENTAL feature \"TestFramework\"."),
                ("BCP050", DiagnosticLevel.Error, @"The specified path is empty."),
                ("BCP018", DiagnosticLevel.Error, "Expected the \"{\" character at this location."),
            });
            result = CompilationHelper.Compile(@"
test test1 ''
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP348", DiagnosticLevel.Error, "Using a test declaration statement requires enabling EXPERIMENTAL feature \"TestFramework\"."),
                ("BCP050", DiagnosticLevel.Error, @"The specified path is empty."),
                ("BCP018", DiagnosticLevel.Error, "Expected the \"=\" character at this location."),
            });
            result = CompilationHelper.Compile(@"
test test1
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP348", DiagnosticLevel.Error, "Using a test declaration statement requires enabling EXPERIMENTAL feature \"TestFramework\"."),
                ("BCP347", DiagnosticLevel.Error,  "Expected a test path string at this location."),
                ("BCP358", DiagnosticLevel.Error, "This declaration is missing a template file path reference.")
            });
            result = CompilationHelper.Compile(@"
test
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP348", DiagnosticLevel.Error, "Using a test declaration statement requires enabling EXPERIMENTAL feature \"TestFramework\"."),
                ("BCP346", DiagnosticLevel.Error,  "Expected a test identifier at this location."),
                ("BCP358", DiagnosticLevel.Error, "This declaration is missing a template file path reference.")
            });
        }

        [TestMethod]
        public void TestFramework_statement_parse_diagnostics_are_guiding()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP346", DiagnosticLevel.Error,  "Expected a test identifier at this location."),
                ("BCP358", DiagnosticLevel.Error,  "This declaration is missing a template file path reference.")

            });

            result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test test1
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP347", DiagnosticLevel.Error,  "Expected a test path string at this location."),
                ("BCP358", DiagnosticLevel.Error, "This declaration is missing a template file path reference.")
            });

            result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test test1 ''
");
            result.Should().HaveDiagnostics(new[] {
                ("BCP050", DiagnosticLevel.Error, @"The specified path is empty."),
                ("BCP018", DiagnosticLevel.Error, "Expected the \"=\" character at this location."),
            });

        }
        [TestMethod]
        public void TestFramework_should_not_have_diagnostics_on_required_parameters()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                                                    ("testMain.bicep", @"
                                                param name string
                                                "),
                                                    ("main.bicep", @"
                                                test foo 'testMain.bicep' = {
                                                params: {
                                                    name: 'us'
                                                }
                                                }
                                                "));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }
        [TestMethod]
        public void TestFramework_should_have_diagnostics_on_wrong_parameter_type()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                                                    ("main.bicep", @"
                                                test foo 'testMain.bicep' = {
                                                params: {
                                                    name: 1
                                                }
                                                }
                                                "), ("testMain.bicep", @"
                                                param name string
                                                "));

            result.Should().HaveDiagnostics(new[] {
            ("BCP036", DiagnosticLevel.Error, "The property \"name\" expected a value of type \"string\" but the provided value is of type \"1\"."),
        });
        }

        [TestMethod]
        public void TestFramework_should_have_diagnostics_when_missing_parameters()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                                                    ("main.bicep", @"
                                                test foo 'testMain.bicep' = {
                                                params: {
                                                }
                                                }
                                                "), ("testMain.bicep", @"
                                                param name string
                                                "));

            result.Should().HaveDiagnostics(new[] {
        ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"name\"."),
        });
        }

        [TestMethod]
        public void TestFramework_should_have_diagnostics_when_missing_parameters_property()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                                                    ("main.bicep", @"
                                                test foo 'testMain.bicep' = {
                                                }
                                                "), ("testMain.bicep", @"
                                                param name string
                                                "));

            result.Should().HaveDiagnostics(new[] {
        ("BCP035", DiagnosticLevel.Error, "The specified \"test\" declaration is missing the following required properties: \"params\"."),
        });
        }

        [TestMethod]
        public void Targetless_test_requires_a_match_selector()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"test\" declaration is missing the following required properties: \"match\"."),
            });
        }

        [TestMethod]
        public void Match_selector_requires_include_patterns()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"include\"."),
            });
        }

        [TestMethod]
        public void Match_selector_rejects_an_empty_include_list()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: []
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP459", DiagnosticLevel.Error, "The \"match\" selector must declare at least one \"include\" pattern."),
            });
        }

        [TestMethod]
        public void Match_selector_rejects_patterns_that_escape_the_root()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['../*.bicep', '/rooted.bicep']
    exclude: ['sub/../../*.bicep']
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP460", DiagnosticLevel.Error, "The pattern \"../*.bicep\" must not be rooted or contain \"..\" segments. Use \"root\" to select a different directory."),
                ("BCP460", DiagnosticLevel.Error, "The pattern \"/rooted.bicep\" must not be rooted or contain \"..\" segments. Use \"root\" to select a different directory."),
                ("BCP460", DiagnosticLevel.Error, "The pattern \"sub/../../*.bicep\" must not be rooted or contain \"..\" segments. Use \"root\" to select a different directory."),
            });
        }

        [TestMethod]
        public void Match_selector_cannot_be_combined_with_a_literal_target_path()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                ("main.bicep", @"
test foo 'testMain.bicep' = {
  match: {
    include: ['*.bicep']
  }
  params: {
    name: 'us'
  }
}
"), ("testMain.bicep", @"
param name string
"));

            result.Should().HaveDiagnostics(new[] {
                ("BCP458", DiagnosticLevel.Error, "A test declares its targets either as a literal path or through a \"match\" selector, but not both. Remove the literal path to select targets dynamically."),
            });
        }

        [TestMethod]
        public void Targetless_test_with_a_valid_selector_has_no_diagnostics()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    root: 'modules'
    include: ['*.bicep']
    exclude: ['skip.bicep']
    allowEmpty: true
  }
  params: {
    name: 'us'
  }
}
");

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void Targetless_test_selector_values_must_be_compile_time_constants()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
param patterns array = []

test foo = {
  match: {
    include: patterns
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP032", DiagnosticLevel.Error, "The value must be a compile-time constant."),
            });
        }
    }

}
