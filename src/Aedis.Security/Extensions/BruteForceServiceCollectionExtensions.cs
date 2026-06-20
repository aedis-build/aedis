using Aedis.Security.Abstractions;
using Aedis.Security.BruteForce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra a proteção contra força bruta por credencial (<see cref="IBruteForceGuard" />) sobre o
///     <c>ICache</c> do Aedis. Requer um <c>ICache</c> registrado (ex.: <c>Aedis.Cache.Redis</c>) para que a
///     contagem e o bloqueio sejam distribuídos entre as instâncias.
/// </summary>
public static class BruteForceServiceCollectionExtensions
{
    /// <summary>
    ///     Vincula <see cref="BruteForceOptions" /> à seção <c>Security:BruteForce</c> e registra o
    ///     <see cref="CacheBruteForceGuard" /> como <see cref="IBruteForceGuard" />.
    /// </summary>
    public static IServiceCollection AddAedisBruteForceGuard(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<BruteForceOptions>(configuration.GetSection(BruteForceOptions.SectionName));
        services.TryAddSingleton<IBruteForceGuard, CacheBruteForceGuard>();
        return services;
    }
}
