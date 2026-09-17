// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Configuration;
using Bicep.Core.Diagnostics;
using Bicep.Core.Features;
using Bicep.Core.Syntax;
using Bicep.IO.Abstraction;

namespace Bicep.Core.SourceGraph
{
    /// <summary>
    /// A Bicep test parameters file. It binds to one test file with "using" and supplies named
    /// input cases for it. Part of the experimental test framework.
    /// </summary>
    public class BicepTestParamFile : BicepSourceFile
    {
        public BicepTestParamFile(
            IFileHandle fileHandle,
            ImmutableArray<int> lineStarts,
            ProgramSyntax programSyntax,
            IBicepConfigurationManager configurationManager,
            IFeatureProviderFactory featureProviderFactory,
            IAuxiliaryFileCache auxiliaryFileCache,
            IDiagnosticLookup lexingErrorLookup,
            IDiagnosticLookup parsingErrorLookup)
            : base(
                  fileHandle,
                  lineStarts,
                  programSyntax,
                  configurationManager,
                  featureProviderFactory,
                  auxiliaryFileCache,
                  lexingErrorLookup,
                  parsingErrorLookup)
        {
        }

        private BicepTestParamFile(BicepTestParamFile original) : base(original)
        {
        }

        public override BicepSourceFileKind FileKind => BicepSourceFileKind.TestParamsFile;

        public override BicepSourceFile ShallowClone() => new BicepTestParamFile(this);
    }
}
