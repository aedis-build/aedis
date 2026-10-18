using Microsoft.Extensions.DependencyInjection;

namespace Aedis.AI.Abstractions;

/// <summary>
///     Ajustes fluentes após <c>AddAedisAI</c>. Pacotes de provider estendem este builder (ex.:
///     <c>WithManagedAgents()</c>) registrando o <see cref="IAgentEngine" />.
/// </summary>
public sealed class AIBuilder(IServiceCollection services)
{
    /// <summary>Coleção de serviços em configuração.</summary>
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
