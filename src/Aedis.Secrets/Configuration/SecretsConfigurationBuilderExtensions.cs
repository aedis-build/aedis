using Aedis.Secrets.Abstractions;
using Aedis.Secrets.Configuration;

namespace Microsoft.Extensions.Configuration;

/// <summary>Registro da fonte de configuração baseada em cofre de segredos do Aedis.</summary>
public static class SecretsConfigurationBuilderExtensions
{
    /// <summary>
    ///     Adiciona ao <see cref="IConfigurationBuilder" /> uma fonte que, no startup, carrega segredos do
    ///     <paramref name="provider" /> para o <see cref="IConfiguration" /> conforme <paramref name="mappings" />
    ///     (<c>chave-de-config → nome-do-segredo</c>). Como roda antes do contêiner de DI, recebe um cofre já
    ///     construído (ex.: <c>AwsSecretsManagerProvider.Create(options)</c>).
    /// </summary>
    /// <param name="builder">Builder de configuração ao qual a fonte é adicionada.</param>
    /// <param name="provider">Cofre de onde os segredos são lidos.</param>
    /// <param name="mappings">Mapa <c>chave-de-config → nome-do-segredo</c>.</param>
    /// <param name="optional">Quando <c>false</c> (padrão), um segredo ausente lança no startup.</param>
    public static IConfigurationBuilder AddAedisSecrets(this IConfigurationBuilder builder, ISecretsProvider provider,
        IReadOnlyDictionary<string, string> mappings, bool optional = false) {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(mappings);
        return builder.Add(new SecretsConfigurationSource(provider, mappings, optional));
    }
}
