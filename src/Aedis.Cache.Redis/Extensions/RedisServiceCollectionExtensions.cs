using Aedis.Cache.Abstractions;
using Aedis.Cache.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider Redis do Aedis.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="ICache" /> sobre Redis (singleton, conexão única por processo) e o
    ///     health check <c>redis</c> com a tag <c>ready</c>. Lê as opções da seção <c>REDIS</c>.
    ///     Os locks distribuídos obtidos via <see cref="ICache.IsLeaderAsync" /> devem ser registrados
    ///     no <c>IDisposableRegistry</c> (de <c>AddAedisDiagnostics()</c>) para liberação automática no
    ///     desligamento gracioso.
    /// </summary>
    public static IServiceCollection AddAedisRedis(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<RedisCacheOptions>()
            .Bind(configuration.GetSection(RedisCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<RedisCache>();
        services.TryAddSingleton<ICache>(sp => sp.GetRequiredService<RedisCache>());

        services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }
}
