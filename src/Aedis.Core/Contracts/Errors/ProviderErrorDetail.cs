using MessagePack;

namespace Aedis.Core.Errors;

/// <summary>
///     Detalhe individual de erro (para cenários multi-item como OptIn com múltiplos arranjos)
/// </summary>
[MessagePackObject]
public record ProviderErrorDetail
{
    /// <summary>
    ///     Identificador do item com erro (ex: codigoExterno, arranjo)
    /// </summary>
    [Key(0)]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    ///     ID do item no provedor (ex: protocolo/ID no provedor)
    /// </summary>
    [Key(1)]
    public string? ProviderItemId { get; set; }

    /// <summary>
    ///     Código de erro específico deste item
    /// </summary>
    [Key(2)]
    public string? ErrorCode { get; set; }

    /// <summary>
    ///     Mensagem de erro deste item
    /// </summary>
    [Key(3)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    ///     Campo(s) que causaram o erro
    /// </summary>
    [Key(4)]
    public IReadOnlyList<string>? Fields { get; set; }

    /// <summary>
    ///     Status específico deste item (SUCESSO, ERRO, PENDENTE)
    /// </summary>
    [Key(5)]
    public string Status { get; set; } = "ERRO";

    /// <summary>
    ///     Timestamp específico deste item (se disponível)
    /// </summary>
    [Key(6)]
    public DateTimeOffset? Timestamp { get; set; }
}