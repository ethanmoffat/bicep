// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Core.SourceGraph
{
    // TODO: Move to a more common namespace
    public enum BicepSourceFileKind
    {
        /// <summary>
        /// A Bicep file containing parameter declarations, resources, variables, outputs,
        /// and references to other modules. This is sometimes known as a Bicep module.
        /// </summary>
        BicepFile,

        /// <summary>
        /// A Bicep parameters file that may contain a reference to a Bicep file and may
        /// also set values of the parameters declared in the referenced Bicep file.
        /// </summary>
        ParamsFile,

        /// <summary>
        /// A Bicep file used in the REPL environment.
        /// </summary>
        ReplFile,

        /// <summary>
        /// A Bicep test file that declares tests referencing other Bicep files. Part of the
        /// experimental test framework and only meaningful when that feature is enabled.
        /// </summary>
        TestFile,

        /// <summary>
        /// A Bicep test parameters file that binds to a test file with "using" and supplies
        /// named input cases for it. Part of the experimental test framework.
        /// </summary>
        TestParamsFile
    }
}
