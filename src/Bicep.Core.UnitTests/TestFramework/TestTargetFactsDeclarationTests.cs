// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TestFramework;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.TestFramework;

[TestClass]
public class TestTargetFactsDeclarationTests
{
    private static TestTargetFacts Collect(params (string fileName, string fileContents)[] files)
    {
        var result = CompilationHelper.Compile(files);
        var model = result.Compilation.GetEntrypointSemanticModel();
        var root = model.SourceFile.FileHandle.GetParent().Uri;

        return TestTargetFactsCollector.Collect(model, root);
    }

    [TestMethod]
    public void Collect_ReportsDeclaredVariablesInSourceOrderWithWhetherTheyAreExported()
    {
        // An imported variable is not a declaration of this file, so it is reported only as an import.
        var facts = Collect(
            ("main.bicep", """
import { shared } from 'shared.bicep'

var prefix = 'app'

@export()
var suffix = '${prefix}-x'
var names = [for i in range(0, 2): '${prefix}${i}${shared}']
"""),
            ("shared.bicep", """
@export()
var shared = 's'
"""));

        facts.Local.Variables.Select(x => (x.Name, x.Exported, x.File, x.Line)).Should().Equal(
            ("prefix", false, "main.bicep", 3),
            ("suffix", true, "main.bicep", 5),
            ("names", false, "main.bicep", 7));
    }

    [TestMethod]
    public void Collect_IncludesModuleVariablesOnlyWithModules()
    {
        var facts = Collect(
            ("main.bicep", """
var top = 'top'

module child 'modules/child.bicep' = {
  name: 'child'
  params: { value: top }
}
"""),
            ("modules/child.bicep", """
param value string
var inner = toUpper(value)
output upper string = inner
"""));

        facts.Local.Variables.Select(x => x.Name).Should().Equal("top");
        facts.WithModules.Variables.Select(x => (x.Name, x.File)).Should().Equal(
            ("top", "main.bicep"),
            ("inner", "modules/child.bicep"));
    }

    [TestMethod]
    public void Collect_ReportsParameterBoundsFromTheParameterAndItsTypeAlias()
    {
        // The parameter's own decorator wins over its alias's, the same rule the compiler validates by.
        var facts = Collect(("main.bicep", """
@minLength(3)
@maxLength(63)
type accountName = string

param name accountName
@maxLength(24)
param shortName accountName
@minLength(1)
param zones string[]?
@minValue(1)
@maxValue(10)
param count int
param plain string
param pair [string, string]
"""));

        facts.Local.Parameters.Select(x => (x.Name, x.MinLength, x.MaxLength, x.MinValue, x.MaxValue)).Should().Equal(
            ("name", 3L, 63L, (long?)null, (long?)null),
            ("shortName", 3L, 24L, null, null),
            ("zones", 1L, null, null, null),
            ("count", null, null, 1L, 10L),
            ("plain", null, null, null, null),
            ("pair", null, null, null, null));
    }

    [TestMethod]
    public void Collect_ReportsAllowedValuesOnlyFromTheDecorator()
    {
        var facts = Collect(("main.bicep", """
@allowed(['Standard', 'Premium'])
param tier string
@allowed([1, 3, -1])
param retention int?
param kind 'a' | 'b'
"""));

        var allowed = facts.Local.Parameters.ToDictionary(x => x.Name, x => x.Allowed);
        allowed["tier"]!.Value.Select(x => x.ToString()).Should().Equal("Standard", "Premium");
        allowed["retention"]!.Value.Select(x => (long)x).Should().Equal(1, 3, -1);
        allowed["kind"].Should().BeNull();
    }
}
