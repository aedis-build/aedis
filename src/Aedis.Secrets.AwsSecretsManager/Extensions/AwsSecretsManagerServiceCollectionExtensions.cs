using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Aedis.Secrets.AwsSecretsManager;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro de DI do provider de segredos AWS Secrets Manager do Aedis.</summary>
public static class AwsSecretsManagerServiceCollectionExtensions
{
    /// <summary>
    ///     Registra o <see cref="ISecretsProvider" /> sobre o AWS Secrets Manager (com cache em memória via
    ///     <c>CachingSecretsProvider</c>) e o health check <c>secrets</c> com a tag <c>ready</c>. Lê as opções
    ///     da seção <c>AwsSecretsManager</c> e o TTL de cache da seção <c>Secrets</c>. O cliente usa a cadeia
    ///     de credenciais padrão da AWS, salvo override explícito nas opções.
    /// </summary>
    public static IServiceCollection AddAedisAwsSecretsManager(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<AwsSecretsManagerOptions>()
            .Bind(configuration.GetSection(AwsSecretsManagerOptions.SectionName));
        services.AddOptions<SecretsCachingOptions>()
            .Bind(configuration.GetSection(SecretsCachingOptions.SectionName));

        services.TryAddSingleton<IAmazonSecretsManager>(sp =>
            AwsSecretsManagerClientFactory.Build(sp.GetRequiredService<IOptions<AwsSecretsManagerOptions>>().Value));

        services.AddAedisSecretsCaching<AwsSecretsManagerProvider>();

        services.AddHealthChecks()
            .AddCheck<AwsSecretsManagerHealthCheck>("secrets", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
