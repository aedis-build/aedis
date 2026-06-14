using MessagePack;

namespace Aedis.Core.Errors;

/// <summary>
///     Resposta de erro padronizada baseada em RFC 7807 - Problem Details for HTTP APIs
/// </summary>
[MessagePackObject]
public record ApiErrorResponse
{
    /// <summary>
    ///     URI que identifica o tipo de problema (ex: "https://api.example.com/errors/validation")
    /// </summary>
    [Key(0)]
    public string Type { get; set; } = "about:blank";

    /// <summary>
    ///     Título curto e legível do erro (ex: "Validation Error")
    /// </summary>
    [Key(1)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     Código HTTP do erro (400, 422, 500, etc)
    /// </summary>
    [Key(2)]
    public int Status { get; set; }

    /// <summary>
    ///     Descrição detalhada específica desta ocorrência do erro
    /// </summary>
    [Key(3)]
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    ///     URI da instância específica do erro (ex: log trace URL)
    /// </summary>
    [Key(4)]
    public string? Instance { get; set; }

    /// <summary>
    ///     Timestamp ISO 8601 de quando o erro ocorreu
    /// </summary>
    [Key(5)]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Correlation ID para rastreamento end-to-end
    /// </summary>
    [Key(6)]
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Trace ID (OpenTelemetry/distributed tracing)
    /// </summary>
    [Key(7)]
    public string? TraceId { get; set; }

    /// <summary>
    ///     Campos adicionais específicos do domínio
    /// </summary>
    [Key(8)]
    public IDictionary<string, object>? Extensions { get; set; }
}