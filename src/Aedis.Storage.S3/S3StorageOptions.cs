namespace Aedis.Storage.S3;

/// <summary>
///     Configuração de conexão para um bucket S3 (ou S3-compatível, ex.: MinIO).
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>Nome do bucket (obrigatório).</summary>
    public required string BucketName { get; set; }

    /// <summary>Access key. Se vazio, usa a cadeia de credenciais padrão da AWS.</summary>
    public string? AccessKey { get; set; }

    /// <summary>Secret key.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Endpoint customizado (ex.: MinIO). Se vazio, usa a região da AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Força path-style addressing (necessário em alguns S3-compatíveis).</summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Prefixo opcional aplicado a todas as chaves.</summary>
    public string Prefix { get; set; } = string.Empty;
}
