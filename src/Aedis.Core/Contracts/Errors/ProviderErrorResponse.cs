using MessagePack;

namespace Aedis.Core.Errors;

/// <summary>
///     Resposta de erro padronizada para integrações com provedores externos
///     Estende ApiErrorResponse (RFC 7807) com informações específicas de providers
/// </summary>
[MessagePackObject]
public record ProviderErrorResponse : ApiErrorResponse
{
    /// <summary>
    ///     Nome do provedor (B3, NUCLEA, CERC, etc)
    /// </summary>
    [Key(9)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    ///     ID único retornado pelo provedor (protocol, transaction ID, etc)
    /// </summary>
    [Key(10)]
    public string? ProviderId { get; set; }

    /// <summary>
    ///     Código de erro específico do provedor
    /// </summary>
    [Key(11)]
    public string? ProviderErrorCode { get; set; }

    /// <summary>
    ///     Mensagem original do provedor (não traduzida)
    /// </summary>
    [Key(12)]
    public string? ProviderMessage { get; set; }

    /// <summary>
    ///     Detalhes granulares dos erros (para múltiplos itens)
    /// </summary>
    [Key(13)]
    public IReadOnlyList<ProviderErrorDetail>? Errors { get; set; }

    /// <summary>
    ///     Contexto adicional da requisição
    /// </summary>
    [Key(14)]
    public ProviderErrorContext? Context { get; set; }

    /// <summary>
    ///     Indica se o erro é recuperável/retryable.
    ///     ATENÇÃO: Esta é apenas uma flag informativa - NÃO executa retry automático.
    ///     Implemente retry usando Polly ou outra estratégia em camada superior.
    /// </summary>
    [Key(15)]
    public bool IsRetryable { get; set; }

    /// <summary>
    ///     Categoria do erro
    /// </summary>
    [Key(16)]
    public ErrorCategory Category { get; set; }

    /// <summary>
    ///     Body raw da resposta (opcional, para debug)
    /// </summary>
    [Key(17)]
    public string? RawResponseBody { get; set; }
}