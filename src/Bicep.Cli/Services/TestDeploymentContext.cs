// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core;
using Bicep.Core.Utils;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

/// <summary>
/// The ambient context an evaluation runs with.
///
/// This is runner-owned metadata supplied by the input file, not data a test can read. It is
/// simulated: setting a resource group location does not deploy anything, does not query Azure, and
/// does not implicitly assign a production parameter of the same name.
/// </summary>
public record TestDeploymentContext(
    string? TenantId = null,
    string? ManagementGroup = null,
    string? SubscriptionId = null,
    string? ResourceGroup = null,
    string? ResourceGroupLocation = null)
{
    public static readonly TestDeploymentContext Empty = new();

    /// <summary>
    /// Replaces individual properties. Properties the override does not supply are inherited
    /// unchanged; there is no deep merge, and no property can be cleared by inheriting.
    /// </summary>
    public TestDeploymentContext With(TestDeploymentContext overrides)
        => new(
            overrides.TenantId ?? TenantId,
            overrides.ManagementGroup ?? ManagementGroup,
            overrides.SubscriptionId ?? SubscriptionId,
            overrides.ResourceGroup ?? ResourceGroup,
            overrides.ResourceGroupLocation ?? ResourceGroupLocation);

    public TestDeploymentContext WithProperty(string name, string value) => name switch
    {
        LanguageConstants.DeploymentContextTenantIdPropertyName => this with { TenantId = value },
        LanguageConstants.DeploymentContextManagementGroupPropertyName => this with { ManagementGroup = value },
        LanguageConstants.DeploymentContextSubscriptionIdPropertyName => this with { SubscriptionId = value },
        LanguageConstants.DeploymentContextResourceGroupPropertyName => this with { ResourceGroup = value },
        LanguageConstants.DeploymentContextResourceGroupLocationPropertyName => this with { ResourceGroupLocation = value },
        _ => this,
    };

    public static TestDeploymentContext FromObject(JObject source)
    {
        var context = Empty;

        foreach (var property in source.Properties())
        {
            if (property.Value.Type is JTokenType.String && property.Value.Value<string>() is { } value)
            {
                context = context.WithProperty(property.Name, value);
            }
        }

        return context;
    }

    /// <summary>
    /// Applies the supplied properties to the evaluator's configuration. Unsupplied properties keep
    /// the evaluator's own placeholders rather than being invented here, so a test that never states
    /// a context cannot silently depend on one.
    /// </summary>
    public TemplateEvaluator.EvaluationConfiguration Apply(TemplateEvaluator.EvaluationConfiguration configuration)
        => configuration with
        {
            TenantId = TenantId ?? configuration.TenantId,
            ManagementGroup = ManagementGroup ?? configuration.ManagementGroup,
            SubscriptionId = SubscriptionId ?? configuration.SubscriptionId,
            ResourceGroup = ResourceGroup ?? configuration.ResourceGroup,
            RgLocation = ResourceGroupLocation ?? configuration.RgLocation,
        };
}
