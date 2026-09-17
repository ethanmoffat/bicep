// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using Bicep.Core.DataFlow;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Visitors;

namespace Bicep.Core.Emit
{
    public class EmitterContext
    {
        /// <param name="settings">
        /// Overrides the model's own emitter settings. Offline tooling that has to attribute an emitted
        /// resource back to the declaration it came from needs symbolic names even when the model would
        /// not otherwise emit them.
        /// </param>
        public EmitterContext(SemanticModel semanticModel, EmitterSettings? settings = null)
        {
            Settings = settings ?? semanticModel.EmitterSettings;
            SemanticModel = semanticModel;
            DataFlowAnalyzer = new(semanticModel);
            ResourceDependencies = ResourceDependencyVisitor.GetResourceDependencies(semanticModel);
            FunctionVariables = FunctionVariableGeneratorVisitor.GetFunctionVariables(semanticModel);
        }

        public EmitterSettings Settings { get; }

        public SemanticModel SemanticModel { get; }

        public DataFlowAnalyzer DataFlowAnalyzer { get; }

        public ImmutableDictionary<DeclaredSymbol, ImmutableHashSet<ResourceDependency>> ResourceDependencies { get; }

        public ImmutableDictionary<FunctionCallSyntaxBase, FunctionVariable> FunctionVariables { get; }
    }
}
