using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Aedis.Secrets.AzureKeyVault;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro de DI do provider de segredos Azure Key Vault do Aedis.</summary>
public static class AzureKeyVaultServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="ISecretsProvider" /> sobre o Azure Key Vault (com cache em memória via
    ///     <c>CachingSecretsProvider</c>) e o health check <c>secrets</c> com a tag <c>ready</c>. Lê a URI do
    ///     cofre da seção <c>AzureKeyVault</c> e o TTL de cache da seção <c>Secrets</c>. A autenticação usa
    ///     <c>DefaultAzureCredential</c> (cadeia padrão do Azure).
    /// </summary>
    public static IServiceCollection AddAedisAzureKeyVault(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<AzureKeyVaultOptions>()
            .Bind(configuration.GetSection(AzureKeyVaultOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.VaultUri), "AzureKeyVault:VaultUri é obrigatório.")
            .ValidateOnStart();
        services.AddOptions<SecretsCachingOptions>()
            .Bind(configuration.GetSection(SecretsCachingOptions.SectionName));

        services.TryAddSingleton(sp =>
            AzureKeyVaultClientFactory.Build(sp.GetRequiredService<IOptions<AzureKeyVaultOptions>>().Value));

        services.AddAedisSecretsCaching<AzureKeyVaultProvider>();

        services.AddHealthChecks()
            .AddCheck<AzureKeyVaultHealthCheck>("secrets", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
