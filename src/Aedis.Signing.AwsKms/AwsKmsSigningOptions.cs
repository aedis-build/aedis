using System.ComponentModel.DataAnnotations;

namespace Aedis.Signing.AwsKms;

/// <summary>
///     Opções do provider de chave no AWS KMS (seção <c>Signing:AwsKms</c>). A chave é resolvida por
///     <see cref="KeyId" /> explícito ou pelo <see cref="KeyAlias" /> (padrão <c>alias/aedis/{ApplicationName}/signing</c>);
///     com <see cref="AutoCreateKey" /> ela é criada quando o alias não existe. Região, endpoint e credenciais
///     caem para a seção <c>Aws</c> quando vazios; sem credenciais o SDK usa a cadeia padrão (IRSA, perfil).
/// </summary>
public sealed class AwsKmsSigningOptions
{
    /// <summary>Nome da seção de configuração (<c>Signing:AwsKms</c>).</summary>
    public const string SectionName = "Signing:AwsKms";

    /// <summary>Chave explícita (KeyId, ARN ou <c>alias/...</c>). Tem precedência sobre o alias.</summary>
    public string? KeyId { get; set; }

    /// <summary>Alias resolvido quando <see cref="KeyId" /> está vazio. Padrão: <c>alias/aedis/{ApplicationName}/signing</c>.</summary>
    public string? KeyAlias { get; set; }

    /// <summary>
    ///     Se a chave do alias não existe, cria (<c>ECC_NIST_P256</c>, <c>SIGN_VERIFY</c>) e aponta o alias. Exige
    ///     <c>kms:CreateKey/CreateAlias/TagResource</c>. Em produção costuma ficar desligado, com a chave
    ///     provisionada pela operação no mesmo alias.
    /// </summary>
    public bool AutoCreateKey { get; set; }

    /// <summary>Descrição da chave criada. Padrão: <c>Signing key ({ApplicationName})</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Tags extras da chave criada (mescladas com <c>application</c>/<c>managed-by</c>/<c>purpose</c>).</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>Janela de exclusão da chave órfã criada numa corrida entre réplicas (mínimo da AWS: 7). Padrão 7.</summary>
    [Range(7, 30)]
    public int OrphanKeyDeletionDays { get; set; } = 7;

    /// <summary>Região AWS; vazio cai para <c>Aws:Region</c>.</summary>
    public string? Region { get; set; }

    /// <summary>Endpoint customizado (ex.: emulador local); vazio cai para <c>Aws:ServiceUrl</c>.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Override opcional de access key; vazio cai para <c>Aws:AccessKeyId</c> e depois para a cadeia padrão.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Override opcional de secret key; vazio cai para <c>Aws:SecretAccessKey</c>.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Timeout das chamadas ao KMS, em segundos. Padrão 10.</summary>
    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>Preenche os campos vazios a partir da seção <c>Aws</c>.</summary>
    public void ApplyFallback(string? region, string? serviceUrl, string? accessKeyId, string? secretAccessKey) {
        Region = FirstNonEmpty(Region, region);
        ServiceUrl = FirstNonEmpty(ServiceUrl, serviceUrl);
        AccessKeyId = FirstNonEmpty(AccessKeyId, accessKeyId);
        SecretAccessKey = FirstNonEmpty(SecretAccessKey, secretAccessKey);
    }

    private static string? FirstNonEmpty(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;
}
