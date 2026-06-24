namespace Aedis.Secrets.Abstractions;

/// <summary>
///     Contrato agnóstico de provider para leitura de segredos (credenciais, chaves, connection strings) de
///     um cofre externo — AWS Secrets Manager, HashiCorp Vault, Azure Key Vault, ou a configuração local.
///     Os serviços de mais alto nível do Aedis (cache de segredos, fonte de <c>IConfiguration</c>) constroem-se
///     apenas sobre estas operações; o provider concreto fornece a implementação. As leituras devolvem
///     <c>null</c> quando o segredo não existe — use <c>GetRequiredSecretAsync</c> para falhar explicitamente.
/// </summary>
public interface ISecretsProvider
{
    /// <summary>Lê o valor textual do segredo <paramref name="name" />, ou <c>null</c> se ele não existir.</summary>
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lê o segredo <paramref name="name" /> com metadados (versão, momento da última rotação), ou
    ///     <c>null</c> se ele não existir. Providers que não expõem metadados preenchem apenas o valor.
    /// </summary>
    Task<SecretValue?> GetSecretWithMetadataAsync(string name, CancellationToken cancellationToken = default);
}
