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
    public class TestFrameworkTests
    {
        private ServiceBuilder ServicesWithTestFramework => new ServiceBuilder()
            .WithFeatureOverrides(new(TestContext, TestFrameworkEnabled: true));

        [NotNull]
        public TestContext? TestContext { get; set; }

        /// <summary>
        /// Compiles a '.biceptest' file as the entry point, alongside a trivial target so that the test
        /// declaration itself resolves. Compiling the test file directly is what puts the file-kind
        /// specific rules under test.
        /// </summary>
        private CompilationHelper.CompilationResult CompileTestFile(string testFileContents)
        {
            var fileSet = new MockFileSystemTestFileSet();
            fileSet.AddFile("sample.biceptest", testFileContents);
            fileSet.AddFile("target.bicep", "param name string = 'us'\n");

            return CompilationHelper.Compile(ServicesWithTestFramework, fileSet, fileSet.GetUri("sample.biceptest"));
        }

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
        [TestMethod]
        public void Assertions_can_query_the_compiler_provided_target_facts()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test approvedSql = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    sqlMustBeApproved: {
      failOn: filter(target.resources, r => toLower(r.type) == 'microsoft.sql/servers' && !r.existing && !startsWith(r.file, 'modules/sql/'))
      message: 'Declare SQL servers only inside the approved implementation.'
    }
    mustDeclareSomething: {
      passWhen: !empty(target.withModules.resources) || !empty(target.withModules.modules)
      message: 'The file must declare something.'
    }
  }
}
");

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void Assertions_are_also_available_to_a_test_with_a_literal_target()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                ("main.bicep", @"
test foo 'testMain.bicep' = {
  assertions: {
    noModules: {
      failOn: target.modules
      message: 'The target must not declare modules.'
    }
  }
}
"),
                ("testMain.bicep", @"
param name string = 'us'
"));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void Target_is_not_visible_outside_an_assertion()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
var leaked = target.resources

test foo = {
  match: {
    include: ['*.bicep']
  }
  params: {
    name: target.resources
  }
}
");

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP057", DiagnosticLevel.Error, "The name \"target\" does not exist in the current context."),
                ("BCP057", DiagnosticLevel.Error, "The name \"target\" does not exist in the current context."),
            });
        }

        [TestMethod]
        public void Target_is_not_visible_in_an_ordinary_bicep_file()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
var leaked = target
");

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP057", DiagnosticLevel.Error, "The name \"target\" does not exist in the current context."),
            });
        }

        [TestMethod]
        public void Misspelled_target_facts_are_reported_rather_than_silently_empty()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      failOn: target.resourcez
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP083", DiagnosticLevel.Error, "The type \"target\" does not contain property \"resourcez\". Did you mean \"resources\"?"),
            });
        }

        [TestMethod]
        public void Misspelled_fact_properties_are_reported()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      failOn: filter(target.resources, r => r.typ == 'x')
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"resourceFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP083", DiagnosticLevel.Error, "The type \"resourceFact\" does not contain property \"typ\". Did you mean \"type\"?"),
            });
        }

        [TestMethod]
        public void Declared_contract_facts_are_typed()
        {
            // Misspelt fields are reported, and the scope is one of the values targetScope accepts
            // rather than an open string, so negating it names exactly those values.
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    parameterTypo: {
      failOn: filter(target.parameters, p => p.requird)
      message: 'oops'
    }
    outputTypo: {
      failOn: filter(target.withModules.outputs, o => o.nam == 'x')
      message: 'oops'
    }
    scopeIsNotABool: {
      passWhen: !target.targetScope
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"parameterFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP083", DiagnosticLevel.Error, "The type \"parameterFact\" does not contain property \"requird\". Did you mean \"required\"?"),
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"outputFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP083", DiagnosticLevel.Error, "The type \"outputFact\" does not contain property \"nam\". Did you mean \"name\"?"),
                ("BCP044", DiagnosticLevel.Error, "Cannot apply operator \"!\" to operand of type \"'local' | 'managementGroup' | 'resourceGroup' | 'subscription' | 'tenant'\"."),
            });
        }

        [TestMethod]
        public void Deployment_order_facts_are_typed()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    resourceTypo: {
      failOn: filter(target.resources, r => r.waitFor)
      message: 'oops'
    }
    moduleIsAListOfNames: {
      passWhen: !target.modules[0].waitsFor
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"resourceFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP083", DiagnosticLevel.Error, "The type \"resourceFact\" does not contain property \"waitFor\". Did you mean \"waitsFor\"?"),
                ("BCP044", DiagnosticLevel.Error, "Cannot apply operator \"!\" to operand of type \"string[]\"."),
            });
        }

        [TestMethod]
        public void A_source_fact_names_its_declaration_symbolically()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    symbolic: {
      failOn: filter(target.resources, r => r.symbolicName == 'x')
      message: 'oops'
    }
  }
}
");

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void A_source_fact_does_not_pretend_to_know_a_deployed_name()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      failOn: filter(target.resources, r => r.name == 'x')
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"resourceFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP053", DiagnosticLevel.Error, "The type \"resourceFact\" does not contain property \"name\". Available properties include \"existing\", \"file\", \"line\", \"symbolicName\", \"type\", \"waitsFor\"."),
            });
        }

        [TestMethod]
        public void Assertions_can_query_evaluated_instances_and_outputs()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                ("main.bicep", @"
test foo 'testMain.bicep' = {
  params: {
    name: 'contoso'
  }
  assertions: {
    oneInstancePerRegion: {
      passWhen: length(target.evaluated.resources) == 2
      message: 'Expected one instance per region.'
    }
    everythingIsAccountedFor: {
      failOn: filter(target.evaluated.withModules.resources, r => empty(r.name) || empty(r.instanceId) || empty(r.symbolicName) || empty(r.file) || r.line < 1 || empty(r.type))
      message: 'Every evaluated instance is attributed to a declaration.'
    }
    outputIsComputed: {
      passWhen: target.evaluated.outputs.accountName == 'contoso'
      message: 'The output is computed offline.'
    }
  }
}
"),
                ("testMain.bicep", @"
param name string
"));

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void An_evaluated_instance_fact_is_closed_to_misspelling()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      failOn: filter(target.evaluated.resources, r => r.nam == 'x')
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"evaluatedResourceFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP083", DiagnosticLevel.Error, "The type \"evaluatedResourceFact\" does not contain property \"nam\". Did you mean \"name\"?"),
            });
        }

        [TestMethod]
        public void The_evaluated_branch_itself_is_closed_to_misspelling()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      passWhen: empty(target.evaluated.resource)
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP083", DiagnosticLevel.Error, "The type \"evaluated\" does not contain property \"resource\". Did you mean \"resources\"?"),
            });
        }

        [TestMethod]
        public void Evaluated_outputs_are_open_because_output_names_depend_on_the_bound_target()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    anyOutputNameCompiles: {
      passWhen: target.evaluated.outputs.whateverTheTargetDeclares != null
      message: 'oops'
    }
  }
}
");

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void An_evaluated_instance_does_not_expose_source_only_facts()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    typo: {
      failOn: filter(target.evaluated.resources, r => r.existing)
      message: 'oops'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP070", DiagnosticLevel.Error, "Argument of type \"evaluatedResourceFact => error\" is not assignable to parameter of type \"(any[, int]) => bool\"."),
                ("BCP053", DiagnosticLevel.Error, "The type \"evaluatedResourceFact\" does not contain property \"existing\". Available properties include \"file\", \"identity\", \"instanceId\", \"kind\", \"line\", \"location\", \"name\", \"properties\", \"sku\", \"symbolicName\", \"tags\", \"type\", \"zones\"."),
            });
        }

        [TestMethod]
        public void An_empty_assertions_object_is_an_authoring_error()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {}
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP462", DiagnosticLevel.Error, "The \"assertions\" object must declare at least one assertion. Remove it entirely to evaluate the assertions declared by the target instead."),
            });
        }

        [TestMethod]
        public void An_assertion_must_declare_exactly_one_condition()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    neither: {
      message: 'nothing to judge'
    }
    both: {
      passWhen: true
      failOn: target.modules
      message: 'ambiguous'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP463", DiagnosticLevel.Error, "An assertion must declare exactly one of \"passWhen\" or \"failOn\"."),
                ("BCP463", DiagnosticLevel.Error, "An assertion must declare exactly one of \"passWhen\" or \"failOn\"."),
            });
        }

        [TestMethod]
        public void An_assertion_must_declare_a_message()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    unexplained: {
      passWhen: true
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"message\"."),
            });
        }

        [TestMethod]
        public void An_assertion_condition_must_be_a_boolean_and_not_coerced()
        {
            // "passWhen" judges a condition, not truthiness. A non-boolean is a type error rather than
            // something the evaluator silently interprets.
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    notABoolean: {
      passWhen: length(target.resources)
      message: 'nonsense'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP036", DiagnosticLevel.Error, "The property \"passWhen\" expected a value of type \"bool\" but the provided value is of type \"int\"."),
            });
        }

        [TestMethod]
        public void An_assertions_offending_facts_must_be_a_collection()
        {
            // "failOn" judges collection emptiness. A bare boolean is a type error, which is what keeps
            // the two forms from collapsing into a single truthiness-based check.
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
test foo = {
  match: {
    include: ['*.bicep']
  }
  assertions: {
    notACollection: {
      failOn: true
      message: 'nonsense'
    }
  }
}
");

            result.Should().HaveDiagnostics(new[] {
                ("BCP036", DiagnosticLevel.Error, "The property \"failOn\" expected a value of type \"array\" but the provided value is of type \"true\"."),
            });
        }

        [TestMethod]
        public void Test_file_parameters_accept_the_decorators_that_constrain_a_type()
        {
            var result = CompileTestFile("""
                        @secure()
                        param password string

                        @minLength(2)
                        @maxLength(5)
                        @allowed(['us', 'eu'])
                        @metadata({ owner: 'platform' })
                        param name string

                        @minValue(1)
                        @maxValue(30)
                        param days int = 7

                        @sealed()
                        type settings = { name: string }

                        test policy 'target.bicep' = {
                          params: {
                            name: '${name}${password}${days}'
                          }
                        }
                        """);

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void Test_file_parameters_do_not_accept_the_decorators_that_shape_a_deployment()
        {
            // Nothing in a test file is deployed or imported, so these stay template-only.
            var result = CompileTestFile("""
                        @batchSize(1)
                        param name string

                        @export()
                        type settings = { name: string }

                        test policy 'target.bicep' = {
                          params: {
                            name: name
                          }
                        }
                        """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP057", DiagnosticLevel.Error, "The name \"batchSize\" does not exist in the current context."),
                ("BCP057", DiagnosticLevel.Error, "The name \"export\" does not exist in the current context."),
            });
        }

        [TestMethod]
        public void A_test_can_be_described_but_not_constrained()
        {
            var result = CompileTestFile("""
                        @description('Every account is named by its caller.')
                        test described 'target.bicep' = {}

                        @sys.description('The same, qualified.')
                        test qualified 'target.bicep' = {}

                        @sys.secure()
                        test secured 'target.bicep' = {}

                        @sys.minLength(1)
                        test constrained 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP471", DiagnosticLevel.Error, "Function \"secure\" cannot be used as a test decorator."),
                ("BCP471", DiagnosticLevel.Error, "Function \"minLength\" cannot be used as a test decorator."),
            });
        }

        [TestMethod]
        public void Mocks_are_declared_in_a_test_file()
        {
            var result = CompileTestFile("""
                        mocks = {
                          identity: {
                            operation: 'reference'
                            resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/demo'
                            apiVersion: '2024-11-30'
                            response: {
                              properties: {
                                principalId: '11111111-1111-1111-1111-111111111111'
                              }
                            }
                          }
                        }

                        test policy 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void A_mock_can_reference_a_declared_test_parameter()
        {
            var result = CompileTestFile("""
                        param principalId string = '11111111-1111-1111-1111-111111111111'

                        mocks = {
                          identity: {
                            operation: 'reference'
                            resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/demo'
                            apiVersion: '2024-11-30'
                            response: {
                              properties: {
                                principalId: principalId
                              }
                            }
                          }
                        }

                        test policy 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().NotHaveAnyDiagnostics();
        }

        [TestMethod]
        public void A_mock_entry_requires_its_matching_metadata()
        {
            var result = CompileTestFile("""
                        mocks = {
                          identity: {
                            operation: 'reference'
                          }
                        }

                        test policy 'target.bicep' = {}
                        """);

            // Without an identity a mock is a wildcard, and a wildcard would answer calls nobody meant
            // to make.
            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"apiVersion\", \"resourceId\"."),
            });
        }

        [TestMethod]
        public void An_unsupported_mock_operation_is_reported()
        {
            var result = CompileTestFile("""
                        mocks = {
                          identity: {
                            operation: 'listSecrets'
                            resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/demo'
                            apiVersion: '2023-01-01'
                          }
                        }

                        test policy 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP036", DiagnosticLevel.Error, "The property \"operation\" expected a value of type \"'listKeys' | 'reference'\" but the provided value is of type \"'listSecrets'\"."),
            });
        }

        [TestMethod]
        public void A_misspelled_mock_field_is_reported()
        {
            var result = CompileTestFile("""
                        mocks = {
                          identity: {
                            operation: 'reference'
                            resourceID: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/demo'
                            apiVersion: '2023-01-01'
                          }
                        }

                        test policy 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"resourceId\"."),
                ("BCP089", DiagnosticLevel.Error, "The property \"resourceID\" is not allowed on objects of type \"mock\". Did you mean \"resourceId\"?"),
            });
        }

        [TestMethod]
        public void Mocks_cannot_be_declared_twice()
        {
            var result = CompileTestFile("""
                        mocks = {}

                        mocks = {}

                        test policy 'target.bicep' = {}
                        """);

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP470", DiagnosticLevel.Error, "A file can declare \"mocks\" only once."),
            });
        }

        [TestMethod]
        public void Mocks_cannot_be_declared_in_a_deployable_file()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework, @"
mocks = {}
");

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP468", DiagnosticLevel.Error, "A \"mocks\" declaration is only supported in a \".biceptest\" file."),
            });
        }

        [TestMethod]
        public void A_test_file_cannot_be_referenced_as_a_deployable_module()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                ("main.bicep", @"
module m 'sample.biceptest' = {
  name: 'm'
}
"),
                ("sample.biceptest", @"
test foo 'target.bicep' = {}
"),
                ("target.bicep", @"
param name string = 'us'
"));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP461", DiagnosticLevel.Error, "A \".biceptest\" file declares tests and is not a deployable template, so it cannot be referenced here. Reference the Bicep file under test instead."),
            });
        }

        [TestMethod]
        public void A_test_file_cannot_be_the_target_of_a_test()
        {
            var result = CompilationHelper.Compile(ServicesWithTestFramework,
                ("main.bicep", @"
test foo 'sample.biceptest' = {}
"),
                ("sample.biceptest", @"
test bar 'target.bicep' = {}
"),
                ("target.bicep", @"
param name string = 'us'
"));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP461", DiagnosticLevel.Error, "A \".biceptest\" file declares tests and is not a deployable template, so it cannot be referenced here. Reference the Bicep file under test instead."),
            });
        }

        [TestMethod]
        public void A_literal_target_needs_parameter_values_only_when_the_test_evaluates_it()
        {
            // Source facts need no values, so a test that only reads them may omit params, or supply
            // some of them, just as a match selector may. Anything that evaluates still needs them all.
            var fileSet = new MockFileSystemTestFileSet();
            fileSet.AddFile("sample.biceptest", @"
test sourceOnly 'target.bicep' = {
  assertions: {
    one: { passWhen: length(target.resources) == 1, message: 'one' }
  }
}

test someValues 'target.bicep' = {
  params: { name: 'abc' }
  assertions: {
    storage: { passWhen: target.resources[0].type == 'Microsoft.Storage/storageAccounts', message: 'one' }
  }
}

test readsEvaluated 'target.bicep' = {
  assertions: {
    one: { passWhen: length(target.evaluated.resources) == 1, message: 'one' }
  }
}

test readsEvaluatedWithSomeValues 'target.bicep' = {
  params: { name: 'abc' }
  assertions: {
    one: { passWhen: length(target.evaluated.resources) == 1, message: 'one' }
  }
}

test mightReadEvaluated 'target.bicep' = {
  assertions: {
    one: { passWhen: length(target[toLower('EVALUATED')].resources) == 1, message: 'one' }
  }
}

test runsTheTargetsAssertions 'target.bicep' = {}
");
            fileSet.AddFile("target.bicep", @"
param name string
param location string
param tier string = 'Standard_LRS'

resource account 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: name
  location: location
  sku: { name: tier }
  kind: 'StorageV2'
}
");

            var result = CompilationHelper.Compile(ServicesWithTestFramework, fileSet, fileSet.GetUri("sample.biceptest"));

            result.ExcludingLinterDiagnostics().Should().HaveDiagnostics(new[] {
                ("BCP035", DiagnosticLevel.Error, "The specified \"test\" declaration is missing the following required properties: \"params\"."),
                ("BCP035", DiagnosticLevel.Error, "The specified \"object\" declaration is missing the following required properties: \"location\"."),
                ("BCP035", DiagnosticLevel.Error, "The specified \"test\" declaration is missing the following required properties: \"params\"."),
                ("BCP035", DiagnosticLevel.Error, "The specified \"test\" declaration is missing the following required properties: \"params\"."),
            });
        }
    }

}
