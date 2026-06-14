using MessagePack;

namespace Aedis.Core.Success;

/// <summary>
///     Metadados essenciais da operação para rastreabilidade
///     Estrutura minimalista com apenas o necessário
/// </summary>
[MessagePackObject]
public record OperationMetadata
{
    /// <summary>
    ///     Timestamp ISO 8601 da operação
    /// </summary>
    [Key(0)]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Correlation ID para rastreamento end-to-end
    /// </summary>
    [Key(1)]
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Trace ID (OpenTelemetry/distributed tracing)
    /// </summary>
    [Key(2)]
    public string? TraceId { get; set; }

    /// <summary>
    ///     ID único da operação no provedor (ex: protocolo de processamento)
    /// </summary>
    [Key(3)]
    public string? ProviderId { get; set; }

    /// <summary>
    ///     Nome do provedor externo (ex.: gateway, API externa, serviço de terceiros)
    /// </summary>
    [Key(4)]
    public string Provider { get; set; } = string.Empty;
}