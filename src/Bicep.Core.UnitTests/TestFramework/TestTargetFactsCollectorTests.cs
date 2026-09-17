// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Semantics;
using Bicep.Core.TestFramework;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.TestFramework;

[TestClass]
public class TestTargetFactsCollectorTests
{
    private static TestTargetFacts Collect(params (string fileName, string fileContents)[] files)
    {
        var result = CompilationHelper.Compile(files);
        var model = result.Compilation.GetEntrypointSemanticModel();
        var root = model.SourceFile.FileHandle.GetParent().Uri;

        return TestTargetFactsCollector.Collect(model, root);
    }

    private static SemanticModel Model(params (string fileName, string fileContents)[] files)
        => CompilationHelper.Compile(files).Compilation.GetEntrypointSemanticModel();

    [TestMethod]
    public void Collect_ReportsResourceTypesWithoutTheirApiVersion()
    {
        var facts = Collect(("main.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'
}
"""));

        facts.Local.Resources.Should().HaveCount(1);
        facts.Local.Resources[0].Name.Should().Be("sql");
        facts.Local.Resources[0].Type.Should().Be("Microsoft.Sql/servers");
        facts.Local.Resources[0].Existing.Should().BeFalse();
        facts.Local.Resources[0].File.Should().Be("main.bicep");
        facts.Local.Resources[0].Line.Should().Be(1);
    }

    [TestMethod]
    public void Collect_DistinguishesExistingReferencesFromNewDeclarations()
    {
        var facts = Collect(("main.bicep", """
resource existingSql 'Microsoft.Sql/servers@2021-11-01' existing = {
  name: 'sql'
}

resource newSql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql2'
  location: 'westus'
}
"""));

        facts.Local.Resources.Select(x => (x.Name, x.Existing))
            .Should().Equal(("existingSql", true), ("newSql", false));
    }

    [TestMethod]
    public void Collect_IncludesNestedResourceDeclarations()
    {
        var facts = Collect(("main.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'

  resource db 'databases' = {
    name: 'db'
    location: 'westus'
  }
}
"""));

        facts.Local.Resources.Select(x => (x.Name, x.Type)).Should().BeEquivalentTo(
        [
            ("sql", "Microsoft.Sql/servers"),
            ("db", "Microsoft.Sql/servers/databases"),
        ]);
    }

    [TestMethod]
    public void Collect_ReportsDeclarationsInsideConditionsAndLoopsWithoutEvaluatingThem()
    {
        // A source policy must see what the file declares. Neither the condition nor the loop count
        // is evaluated, and no parameter values are needed to collect these facts.
        var facts = Collect(("main.bicep", """
param deploySql bool
param names array

resource conditional 'Microsoft.Sql/servers@2021-11-01' = if (deploySql) {
  name: 'sql'
  location: 'westus'
}

resource looped 'Microsoft.Sql/servers@2021-11-01' = [for name in names: {
  name: name
  location: 'westus'
}]
"""));

        facts.Local.Resources.Select(x => x.Name).Should().Equal("conditional", "looped");
    }

    [TestMethod]
    public void Collect_ReportsModuleDeclarationsWithBothWrittenAndResolvedPaths()
    {
        var facts = Collect(
            ("main.bicep", """
module child 'modules/child.bicep' = {
  name: 'child'
}
"""),
            ("modules/child.bicep", "output value string = 'value'"));

        facts.Local.Modules.Should().HaveCount(1);
        facts.Local.Modules[0].Name.Should().Be("child");
        facts.Local.Modules[0].Path.Should().Be("modules/child.bicep");
        facts.Local.Modules[0].ResolvedFile.Should().Be("modules/child.bicep");
        facts.Local.Modules[0].File.Should().Be("main.bicep");
    }

    [TestMethod]
    public void Collect_KeepsAChildsDeclarationsOutOfTheLocalScope()
    {
        var facts = Collect(
            ("main.bicep", """
module child 'child.bicep' = {
  name: 'child'
}
"""),
            ("child.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'
}
"""));

        facts.Local.Resources.Should().BeEmpty();
        facts.WithModules.Resources.Select(x => (x.Name, x.File)).Should().Equal(("sql", "child.bicep"));
    }

    [TestMethod]
    public void Collect_VisitsEachReachableFileOnceButKeepsEveryReferenceSite()
    {
        var facts = Collect(
            ("main.bicep", """
module firstCall 'child.bicep' = {
  name: 'first'
}

module secondCall 'child.bicep' = {
  name: 'second'
}

module other 'other.bicep' = {
  name: 'other'
}
"""),
            ("child.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'
}
"""),
            ("other.bicep", "output value string = 'value'"));

        // The shared child declares its resource once, however many callers reach it...
        facts.WithModules.Resources.Should().HaveCount(1);

        // ...but all three call sites remain distinct.
        facts.WithModules.Modules.Select(x => x.Name).Should().Equal("firstCall", "secondCall", "other");
    }

    [TestMethod]
    public void Collect_ReportsOneFactPerImportStatementRegardlessOfSymbolCount()
    {
        var facts = Collect(
            ("main.bicep", """
import { alpha, beta } from 'exports.bicep'
"""),
            ("exports.bicep", """
@export()
var alpha = 'alpha'

@export()
var beta = 'beta'
"""));

        facts.Local.Imports.Should().HaveCount(1);
        facts.Local.Imports[0].Path.Should().Be("exports.bicep");
        facts.Local.Imports[0].ResolvedFile.Should().Be("exports.bicep");
        facts.Local.Imports[0].Symbols.Should().Equal("alpha", "beta");
        facts.Local.Imports[0].Wildcard.Should().BeFalse();
    }

    [TestMethod]
    public void Collect_ReportsWildcardImportsWithoutNamingSymbols()
    {
        var facts = Collect(
            ("main.bicep", """
import * as everything from 'exports.bicep'
"""),
            ("exports.bicep", """
@export()
var alpha = 'alpha'
"""));

        facts.Local.Imports.Should().HaveCount(1);
        facts.Local.Imports[0].Wildcard.Should().BeTrue();
        facts.Local.Imports[0].Symbols.Should().BeEmpty();
    }

    [TestMethod]
    public void Collect_ReportsPathsRelativeToTheFactRootRatherThanTheTargetFile()
    {
        var model = Model(
            ("main.bicep", """
module child 'modules/child.bicep' = {
  name: 'child'
}
"""),
            ("modules/child.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'
}
"""));

        // Choose a root above the target so that a policy phrased in repository-relative terms
        // keeps working no matter which file inside the tree is currently under test.
        var repositoryRoot = model.SourceFile.FileHandle.GetParent().GetParent()!.Uri;
        var facts = TestTargetFactsCollector.Collect(model, repositoryRoot);

        var directoryName = model.SourceFile.FileHandle.GetParent().Uri.PathSegments[^1];

        facts.WithModules.Resources.Select(x => x.File)
            .Should().Equal($"{directoryName}/modules/child.bicep");
    }

    [TestMethod]
    public void Collect_SeparatesPathSegmentsSoSimilarDirectoryNamesDoNotCollide()
    {
        // A policy phrased as a directory prefix must be able to tell "modules/sql" from
        // "modules/sqlbackup". Reported paths use "/" separators and whole segments, so the
        // distinction survives into the facts rather than depending on the host's path syntax.
        var facts = Collect(
            ("main.bicep", """
module approved 'modules/sql/server.bicep' = {
  name: 'approved'
}

module lookalike 'modules/sqlbackup/server.bicep' = {
  name: 'lookalike'
}
"""),
            ("modules/sql/server.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'approved'
  location: 'westus'
}
"""),
            ("modules/sqlbackup/server.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'lookalike'
  location: 'westus'
}
"""));

        facts.WithModules.Resources.Select(x => x.File).Should().BeEquivalentTo(
            "modules/sql/server.bicep",
            "modules/sqlbackup/server.bicep");
    }

    [TestMethod]
    public void Collect_GivesTheSameResolvedFileToDifferentSpellingsOfOneModule()
    {
        // Two callers may spell the same module differently. Identity comes from what the path
        // resolves to, so a policy comparing resolved files sees one module, not two.
        var facts = Collect(
            ("main.bicep", """
module direct 'modules/child.bicep' = {
  name: 'direct'
}

module viaParent 'modules/nested/../child.bicep' = {
  name: 'viaParent'
}
"""),
            ("modules/child.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql'
  location: 'westus'
}
"""),
            ("modules/nested/placeholder.bicep", "// keeps the nested directory present"));

        facts.Local.Modules.Select(x => x.ResolvedFile).Should().Equal(
            "modules/child.bicep",
            "modules/child.bicep");

        // The module body is still collected once, because traversal dedupes by resolved file.
        facts.WithModules.Resources.Should().HaveCount(1);
    }

    [TestMethod]
    public void Collect_DoesNotConflateModulesThatMerelyShareAFileName()
    {
        var facts = Collect(
            ("main.bicep", """
module first 'modules/a/server.bicep' = {
  name: 'first'
}

module second 'modules/b/server.bicep' = {
  name: 'second'
}
"""),
            ("modules/a/server.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'a'
  location: 'westus'
}
"""),
            ("modules/b/server.bicep", """
resource sql 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'b'
  location: 'westus'
}
"""));

        facts.Local.Modules.Select(x => x.ResolvedFile).Should().Equal(
            "modules/a/server.bicep",
            "modules/b/server.bicep");

        facts.WithModules.Resources.Should().HaveCount(2);
    }
}
