using Aedis.AI.Abstractions;
using Aedis.AI.ManagedAgents;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro do provider Managed Agents no módulo de IA.</summary>
public static class ManagedAgentsAIBuilderExtensions
{
    /// <summary>
    ///     Usa o Claude Managed Agents como <see cref="IAgentEngine" />: vincula <c>AI:ManagedAgents</c>
    ///     (<see cref="ManagedAgentsOptions" />), registra o <see cref="AnthropicClient" /> e o
    ///     <see cref="ManagedAgentsEngine" /> como singletons.
    /// </summary>
    public static AIBuilder WithManagedAgents(this AIBuilder builder, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var services = builder.Services;
        services.AddOptions<ManagedAgentsOptions>()
            .Bind(configuration.GetSection(ManagedAgentsOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton(sp => {
            var apiKey = sp.GetRequiredService<IOptions<ManagedAgentsOptions>>().Value.ApiKey;
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentEngine, ManagedAgentsEngine>();
        return builder;
    }
}
