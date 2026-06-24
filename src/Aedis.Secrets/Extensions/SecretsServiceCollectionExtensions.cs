using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro de DI dos serviços agnósticos de segredos do Aedis.</summary>
public static class SecretsServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="ISecretsProvider" /> que lê do próprio <see cref="IConfiguration" />
    ///     (<see cref="ConfigurationSecretsProvider" />) — fallback para desenvolvimento local sem cofre
    ///     externo. Não aplica cache (a configuração já está em memória). Procura segredos sob
    ///     <paramref name="sectionPrefix" /> (padrão <c>Secrets</c>).
    /// </summary>
    public static IServiceCollection AddAedisConfigurationSecrets(this IServiceCollection services,
        string? sectionPrefix = "Secrets") {
        services.TryAddSingleton<ISecretsProvider>(sp =>
            new ConfigurationSecretsProvider(sp.GetRequiredService<IConfiguration>(), sectionPrefix));
        return services;
    }

    /// <summary>
    ///     Registra o provider interno <typeparamref name="TInner" /> e expõe <see cref="ISecretsProvider" />
    ///     decorado com cache em memória (<see cref="CachingSecretsProvider" />) quando
    ///     <see cref="SecretsCachingOptions.CacheEnabled" /> está ligado. Os providers concretos (ex.: AWS
    ///     Secrets Manager) chamam este helper para herdar o caching sem reimplementá-lo.
    /// </summary>
    public static IServiceCollection AddAedisSecretsCaching<TInner>(this IServiceCollection services)
        where TInner : class, ISecretsProvider {
        services.TryAddSingleton<TInner>();
        services.TryAddSingleton<ISecretsProvider>(sp => {
            var inner = sp.GetRequiredService<TInner>();
            var options = sp.GetService<IOptions<SecretsCachingOptions>>()?.Value ?? new SecretsCachingOptions();
            return options.CacheEnabled ? new CachingSecretsProvider(inner, options.CacheTtl) : inner;
        });
        return services;
    }
}
