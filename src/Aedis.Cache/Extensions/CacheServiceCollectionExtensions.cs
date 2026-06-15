using Aedis.Cache;
using Aedis.Cache.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Serviços de cache agnósticos de provider do Aedis, construídos sobre <see cref="ICache" />.
///     Registre um provider antes (ex.: <c>AddAedisRedis()</c>) e então habilite os serviços desejados.
/// </summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="IBatchCache" /> (checkpoint/retomada e deduplicação de lotes).
    ///     Requer um <see cref="ICache" /> já registrado.
    /// </summary>
    public static IServiceCollection AddAedisBatchCache(this IServiceCollection services) {
        EnsureCacheRegistered(services, nameof(AddAedisBatchCache));
        services.TryAddSingleton<IBatchCache, BatchCacheService>();
        return services;
    }

    /// <summary>
    ///     Registra o <see cref="IExecutionCacheContextFactory" /> (contexto de execução com dedup e
    ///     marcador de última execução). Requer um <see cref="ICache" /> já registrado.
    /// </summary>
    public static IServiceCollection AddAedisExecutionCache(this IServiceCollection services) {
        EnsureCacheRegistered(services, nameof(AddAedisExecutionCache));
        services.TryAddSingleton<IExecutionCacheContextFactory, ExecutionCacheContextFactory>();
        return services;
    }

    private static void EnsureCacheRegistered(IServiceCollection services, string caller) {
        if (services.All(s => s.ServiceType != typeof(ICache)))
            throw new InvalidOperationException(
                $"Nenhum ICache registrado. Chame um provider de cache (ex.: AddAedisRedis()) antes de {caller}().");
    }
}
