using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Opções do provider AWS SQS/SNS do Aedis. Suporta pub/sub (SNS + SQS) e point-to-point (SQS).
///     Lidas da seção <c>Aws</c> da configuração. Credenciais são opcionais — sem elas, usa a cadeia de
///     credenciais do ambiente (IAM Role / IRSA), recomendado em produção.
/// </summary>
public sealed class AwsSqsOptions
{
    /// <summary>Nome da seção de configuração de onde as opções são lidas (<c>Aws</c>).</summary>
    public const string SectionName = "Aws";

    /// <summary>Região AWS (ex.: "sa-east-1"). Sem valor, usa a região do ambiente.</summary>
    public string? Region { get; set; }

    /// <summary>Access Key ID. Opcional — sem ele, usa a cadeia de credenciais do ambiente.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Secret Access Key. Opcional — sem ele, usa a cadeia de credenciais do ambiente.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Endpoint customizado (ex.: LocalStack <c>http://localhost:4566</c>) para testes/dev.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Máximo de mensagens por ReceiveMessage (1–10). Padrão 10.</summary>
    [Range(1, 10)]
    public int MaxNumberOfMessages { get; set; } = 10;

    /// <summary>Long polling em segundos (0–20). Padrão 20.</summary>
    [Range(0, 20)]
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>Visibility timeout em segundos (1–43200). Padrão 30.</summary>
    [Range(1, 43200)]
    public int VisibilityTimeout { get; set; } = 30;

    /// <summary>Usa filas FIFO (.fifo) para ordem estrita. Padrão false (standard).</summary>
    public bool UseFifoQueues { get; set; } = false;

    /// <summary>
    ///     Default ao publicar/assinar quando o recurso não existe: SNS Topic (pub/sub) se true, SQS Queue
    ///     (point-to-point) se false. O provider detecta automaticamente via API quando o recurso existe.
    /// </summary>
    public bool UseTopics { get; set; } = true;

    /// <summary>maxReceiveCount do RedrivePolicy (tentativas antes da DLQ). Padrão 10.</summary>
    [Range(1, 1000)]
    public int MaxRetries { get; set; } = 10;

    /// <summary>Timeout das operações AWS em segundos (1–300). Padrão 30.</summary>
    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Comprime (gzip) o payload no publish quando ele atinge <see cref="CompressionThresholdBytes" />,
    ///     sinalizando <c>Content-Encoding: gzip</c> para o consumer reverter. Reduz custo/latência de objetos
    ///     grandes e ajuda a caber no limite de 256 KB do SQS/SNS. Padrão ligado.
    /// </summary>
    public bool CompressionEnabled { get; set; } = true;

    /// <summary>
    ///     Tamanho mínimo (em bytes) do payload serializado para comprimir. Abaixo disso o gzip não compensa
    ///     (overhead + base64). Padrão 1024.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int CompressionThresholdBytes { get; set; } = 1024;
}
