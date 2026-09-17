// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TestFramework;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Renders collected target facts as the plain data an assertion expression sees. The property names
/// here are the same ones the compiler declares on the 'target' type, so what an author can write and
/// what the evaluator supplies cannot drift apart.
/// </summary>
public static class TestTargetFactsSerializer
{
    public static JObject Serialize(TestTargetFacts facts) => new()
    {
        [TestTargetType.ResourcesPropertyName] = SerializeResources(facts.Local),
        [TestTargetType.ModulesPropertyName] = SerializeModules(facts.Local),
        [TestTargetType.ImportsPropertyName] = SerializeImports(facts.Local),
        [TestTargetType.WithModulesPropertyName] = new JObject
        {
            [TestTargetType.ResourcesPropertyName] = SerializeResources(facts.WithModules),
            [TestTargetType.ModulesPropertyName] = SerializeModules(facts.WithModules),
            [TestTargetType.ImportsPropertyName] = SerializeImports(facts.WithModules),
        },
    };

    private static JArray SerializeResources(TestFactSet facts) => new(facts.Resources.Select(resource => new JObject
    {
        [TestTargetType.SymbolicNamePropertyName] = resource.Name,
        [TestTargetType.TypePropertyName] = resource.Type,
        [TestTargetType.ExistingPropertyName] = resource.Existing,
        [TestTargetType.FilePropertyName] = resource.File,
        [TestTargetType.LinePropertyName] = resource.Line,
    }).ToArray<object>());

    private static JArray SerializeModules(TestFactSet facts) => new(facts.Modules.Select(module => new JObject
    {
        [TestTargetType.SymbolicNamePropertyName] = module.Name,
        [TestTargetType.PathPropertyName] = module.Path,
        [TestTargetType.ResolvedFilePropertyName] = module.ResolvedFile,
        [TestTargetType.FilePropertyName] = module.File,
        [TestTargetType.LinePropertyName] = module.Line,
    }).ToArray<object>());

    private static JArray SerializeImports(TestFactSet facts) => new(facts.Imports.Select(import => new JObject
    {
        [TestTargetType.PathPropertyName] = import.Path,
        [TestTargetType.ResolvedFilePropertyName] = import.ResolvedFile,
        [TestTargetType.SymbolsPropertyName] = new JArray(import.Symbols.ToArray<object>()),
        [TestTargetType.WildcardPropertyName] = import.Wildcard,
        [TestTargetType.FilePropertyName] = import.File,
        [TestTargetType.LinePropertyName] = import.Line,
    }).ToArray<object>());

    /// <summary>
    /// Renders the evaluated branch. The module-inclusive collection is only computed when the assertion
    /// asked for it, so a policy about the selected file alone is never failed by a module it never
    /// mentioned.
    /// </summary>
    public static JObject SerializeEvaluated(TestEvaluatedFactsProvider evaluated, bool includeWithModules)
    {
        var result = new JObject
        {
            [TestTargetType.ResourcesPropertyName] = SerializeEvaluatedResources(evaluated.Local),
            [TestTargetType.OutputsPropertyName] = evaluated.Outputs,
        };

        if (includeWithModules)
        {
            result[TestTargetType.WithModulesPropertyName] = new JObject
            {
                [TestTargetType.ResourcesPropertyName] = SerializeEvaluatedResources(evaluated.WithModules),
            };
        }

        return result;
    }

    private static JArray SerializeEvaluatedResources(IEnumerable<TestEvaluatedResource> resources) => new(resources.Select(resource => new JObject
    {
        [TestTargetType.NamePropertyName] = resource.Name,
        [TestTargetType.TypePropertyName] = resource.Type,
        [TestTargetType.SymbolicNamePropertyName] = resource.SymbolicName,
        [TestTargetType.InstanceIdPropertyName] = resource.InstanceId,
        [TestTargetType.FilePropertyName] = resource.File,
        [TestTargetType.LinePropertyName] = resource.Line,
    }).ToArray<object>());
}
