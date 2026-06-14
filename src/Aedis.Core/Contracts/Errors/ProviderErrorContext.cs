using MessagePack;

namespace Aedis.Core.Errors;

/// <summary>
///     Contexto da requisição que gerou o erro (para troubleshooting)
/// </summary>
[MessagePackObject]
public record ProviderErrorContext
{
    /// <summary>
    ///     Endpoint chamado
    /// </summary>
    [Key(0)]
    public string? Endpoint { get; set; }

    /// <summary>
    ///     Método HTTP
    /// </summary>
    [Key(1)]
    public string? Method { get; set; }

    /// <summary>
    ///     Tempo de resposta em ms
    /// </summary>
    [Key(2)]
    public long? ResponseTimeMs { get; set; }

    /// <summary>
    ///     Headers relevantes (sanitizados - sem dados sensíveis)
    /// </summary>
    [Key(3)]
    public IDictionary<string, string>? Headers { get; set; }

    /// <summary>
    ///     Request ID / Message ID
    /// </summary>
    [Key(4)]
    public string? RequestId { get; set; }
}