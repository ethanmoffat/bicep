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
    /// A Bicep test file. Test files use the same syntax as Bicep files, but are intended to
    /// contain test declarations that reference the Bicep files under test. Part of the
    /// experimental test framework.
    /// </summary>
    public class BicepTestFile : BicepSourceFile
    {
        public BicepTestFile(
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

        private BicepTestFile(BicepTestFile original) : base(original)
        {
        }

        public override BicepSourceFileKind FileKind => BicepSourceFileKind.TestFile;

        public override BicepSourceFile ShallowClone() => new BicepTestFile(this);
    }
}
