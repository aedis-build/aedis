using Aedis.AI.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Ponto de entrada do registro do módulo de IA.</summary>
public static class AIServiceCollectionExtensions
{
    /// <summary>
    ///     Começa o registro do módulo de IA; escolha o provedor do <see cref="IAgentEngine" /> em seguida (ex.:
    ///     <c>services.AddAedisAI().WithManagedAgents(configuration)</c>).
    /// </summary>
    public static AIBuilder AddAedisAI(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);
        return new AIBuilder(services);
    }
}
