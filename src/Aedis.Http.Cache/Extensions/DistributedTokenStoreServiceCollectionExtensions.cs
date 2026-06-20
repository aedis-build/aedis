using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Cache;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra o armazenamento distribuído de token sobre o <c>ICache</c>, substituindo o store em memória
///     default. Requer um <c>ICache</c> registrado (ex.: <c>Aedis.Cache.Redis</c>).
/// </summary>
public static class DistributedTokenStoreServiceCollectionExtensions
{
    /// <summary>Troca o <see cref="ITokenStore" /> pela implementação distribuída sobre <c>ICache</c>.</summary>
    public static IServiceCollection AddAedisDistributedTokenStore(this IServiceCollection services) {
        services.Replace(ServiceDescriptor.Singleton<ITokenStore, DistributedTokenStore>());
        return services;
    }
}
