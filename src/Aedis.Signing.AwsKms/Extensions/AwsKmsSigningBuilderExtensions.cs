using Aedis.Signing;
using Aedis.Signing.Abstractions;
using Aedis.Signing.AwsKms;
using Amazon.KeyManagementService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro do provider AWS KMS no módulo de assinatura.</summary>
public static class AwsKmsSigningBuilderExtensions
{
    /// <summary>Nome do provider (<c>Signing:Provider</c>).</summary>
    public const string ProviderName = "AwsKms";

    /// <summary>
    ///     Usa o AWS KMS como provider da chave de assinatura: vincula <c>Signing:AwsKms</c> (com fallback de
    ///     região/endpoint/credenciais na seção <c>Aws</c>), registra o cliente, o provider de chave, o provedor
    ///     de assinatura e a sondagem do health check, e fixa <c>Signing:Provider = AwsKms</c>.
    /// </summary>
    public static SigningBuilder WithAwsKms(this SigningBuilder builder, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var services = builder.Services;
        services.AddOptions<AwsKmsSigningOptions>()
            .Bind(configuration.GetSection(AwsKmsSigningOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => string.IsNullOrWhiteSpace(o.KeyAlias) || o.KeyAlias.StartsWith("alias/", StringComparison.Ordinal),
                "Signing:AwsKms:KeyAlias deve começar com 'alias/'.")
            .ValidateOnStart();

        var awsSection = configuration.GetSection("Aws");
        services.AddOptions<AwsKmsSigningOptions>().PostConfigure<IOptions<SigningOptions>>((o, signing) => {
            o.ApplyFallback(awsSection["Region"], awsSection["ServiceUrl"], awsSection["AccessKeyId"], awsSection["SecretAccessKey"]);
            var app = signing.Value.ApplicationName ?? "aedis";
            o.KeyAlias = string.IsNullOrWhiteSpace(o.KeyAlias) ? $"alias/aedis/{app}/signing" : o.KeyAlias;
            o.Description ??= $"Signing key ({app})";
        });

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<AwsKmsSigningOptions>>().Value);
        services.TryAddSingleton<IAmazonKeyManagementService>(sp => AwsKmsClientFactory.Build(sp.GetRequiredService<AwsKmsSigningOptions>()));
        services.TryAddSingleton<KmsSigningKeyProvider>();
        services.TryAddKeyedSingleton<ISigningKeyProvider>(ProviderName, (sp, _) => sp.GetRequiredService<KmsSigningKeyProvider>());
        services.TryAddKeyedSingleton<ISignatureProvider>(ProviderName, (sp, _) =>
            new KmsSignatureProvider(sp.GetRequiredService<IAmazonKeyManagementService>(), sp.GetRequiredService<KmsSigningKeyProvider>()));
        services.TryAddSingleton<ISigningKeyProbe>(sp => sp.GetRequiredService<KmsSigningKeyProvider>());

        return builder.UseProvider(ProviderName);
    }
}
