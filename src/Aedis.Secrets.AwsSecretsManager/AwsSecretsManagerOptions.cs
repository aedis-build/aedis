namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Configuração do provider de segredos do AWS Secrets Manager. Por padrão <strong>não</strong> recebe
///     credenciais aqui: o cliente usa a cadeia de credenciais padrão da AWS (variáveis de ambiente, perfil
///     compartilhado, role de IAM/IRSA). <see cref="AccessKeyId" />/<see cref="SecretAccessKey" /> são apenas
///     um override opcional; <see cref="ServiceUrl" /> aponta para um endpoint local (ex.: LocalStack).
/// </summary>
public sealed class AwsSecretsManagerOptions
{
    /// <summary>Nome da seção de configuração (<c>AwsSecretsManager</c>).</summary>
    public const string SectionName = "AwsSecretsManager";

    /// <summary>Região AWS (ex.: <c>us-east-1</c>). Ignorada quando <see cref="ServiceUrl" /> está definido.</summary>
    public string? Region { get; set; }

    /// <summary>Endpoint customizado do serviço (ex.: LocalStack). Quando vazio, usa a região da AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Override opcional de access key. Vazio = cadeia de credenciais padrão da AWS.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Override opcional de secret key. Só usado em conjunto com <see cref="AccessKeyId" />.</summary>
    public string? SecretAccessKey { get; set; }
}
