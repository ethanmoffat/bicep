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
}
