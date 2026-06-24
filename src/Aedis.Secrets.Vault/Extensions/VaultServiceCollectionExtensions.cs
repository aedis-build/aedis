using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Aedis.Secrets.Vault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VaultSharp;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro de DI do provider de segredos HashiCorp Vault do Aedis.</summary>
public static class VaultServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="ISecretsProvider" /> sobre o HashiCorp Vault (KV v2, com cache em memória
    ///     via <c>CachingSecretsProvider</c>) e o health check <c>secrets</c> com a tag <c>ready</c>. Lê as
    ///     opções da seção <c>Vault</c> e o TTL de cache da seção <c>Secrets</c>.
    /// </summary>
    public static IServiceCollection AddAedisVault(this IServiceCollection services, IConfiguration configuration) {
        services.AddOptions<VaultOptions>()
            .Bind(configuration.GetSection(VaultOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Address) && !string.IsNullOrWhiteSpace(options.Token),
                "Vault:Address e Vault:Token são obrigatórios.")
            .ValidateOnStart();
        services.AddOptions<SecretsCachingOptions>()
            .Bind(configuration.GetSection(SecretsCachingOptions.SectionName));

        services.TryAddSingleton<IVaultClient>(sp =>
            VaultClientFactory.Build(sp.GetRequiredService<IOptions<VaultOptions>>().Value));

        services.AddAedisSecretsCaching<VaultSecretsProvider>();

        services.AddHealthChecks()
            .AddCheck<VaultHealthCheck>("secrets", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
