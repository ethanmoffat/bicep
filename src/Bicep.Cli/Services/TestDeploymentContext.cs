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
    string? ResourceGroupLocation = null,
    string? DeploymentName = null,
    string? DeploymentLocation = null)
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
            overrides.ResourceGroupLocation ?? ResourceGroupLocation,
            overrides.DeploymentName ?? DeploymentName,
            overrides.DeploymentLocation ?? DeploymentLocation);

    public TestDeploymentContext WithProperty(string name, string value) => name switch
    {
        LanguageConstants.DeploymentContextTenantIdPropertyName => this with { TenantId = value },
        LanguageConstants.DeploymentContextManagementGroupPropertyName => this with { ManagementGroup = value },
        LanguageConstants.DeploymentContextSubscriptionIdPropertyName => this with { SubscriptionId = value },
        LanguageConstants.DeploymentContextResourceGroupPropertyName => this with { ResourceGroup = value },
        LanguageConstants.DeploymentContextResourceGroupLocationPropertyName => this with { ResourceGroupLocation = value },
        LanguageConstants.DeploymentContextDeploymentNamePropertyName => this with { DeploymentName = value },
        LanguageConstants.DeploymentContextDeploymentLocationPropertyName => this with { DeploymentLocation = value },
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
    {
        var applied = configuration with
        {
            TenantId = TenantId ?? configuration.TenantId,
            ManagementGroup = ManagementGroup ?? configuration.ManagementGroup,
            SubscriptionId = SubscriptionId ?? configuration.SubscriptionId,
            ResourceGroup = ResourceGroup ?? configuration.ResourceGroup,
            RgLocation = ResourceGroupLocation ?? configuration.RgLocation,
        };

        if (DeploymentName is null)
        {
            // No deployment name was stated, so `deployment()` stays unavailable rather than resolving
            // to values nobody chose. A location on its own describes a deployment that does not exist.
            return applied;
        }

        var deployment = new JObject
        {
            ["name"] = DeploymentName,
            ["properties"] = new JObject
            {
                ["mode"] = "Incremental",
            },
        };

        if (DeploymentLocation is not null)
        {
            // Only present when stated. Bicep emits `deployment().location` for a module deployed to
            // another subscription, so a template can need this without ever writing it.
            deployment["location"] = DeploymentLocation;
        }

        return applied with
        {
            Metadata = new Dictionary<string, JToken>(applied.Metadata)
            {
                [DeploymentMetadataName] = deployment,
            },
        };
    }

    private const string DeploymentMetadataName = "deployment";
}
