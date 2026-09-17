// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Emit;
using Bicep.Core.Semantics;
using Microsoft.WindowsAzure.ResourceStack.Common.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// Emits the ARM template of a target so the runner can evaluate it offline.
/// </summary>
public static class TestTemplateEmitter
{
    /// <param name="forceSymbolicNames">
    /// Emits symbolic resource names even when the target would not otherwise need them. Without them an
    /// evaluated instance cannot be attributed to the declaration it came from, which is what makes a
    /// violation point at source rather than at a resolved name.
    /// </param>
    public static JToken Emit(SemanticModel model, bool forceSymbolicNames = false)
    {
        var textWriter = new StringWriter();
        using var writer = new SourceAwareJsonTextWriter(textWriter)
        {
            // don't close the textWriter when writer is disposed
            CloseOutput = false,
            Formatting = Formatting.Indented
        };

        var settings = forceSymbolicNames ? new EmitterSettings(model, forceSymbolicNames: true) : null;
        var (_, template) = new TemplateWriter(model, settings).GetTemplate(writer);

        return template;
    }
}
