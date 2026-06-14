using MessagePack;
using Aedis.Core.Enums;
using Aedis.Core.Errors;

namespace Aedis.Core.Success;

/// <summary>
///     Envelope padrão para TODAS as respostas de operação (sucesso ou erro)
///     Estrutura minimalista focada no essencial: rastreabilidade e clareza
/// </summary>
[MessagePackObject]
public record OperationResult<TData>
{
    /// <summary>
    ///     Indica se a operação foi bem-sucedida
    /// </summary>
    [Key(0)]
    public bool Success { get; set; }

    /// <summary>
    ///     Mensagem amigável descrevendo o resultado
    /// </summary>
    [Key(1)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    ///     Dados da resposta (quando Success = true)
    /// </summary>
    [Key(2)]
    public TData? Data { get; set; }

    /// <summary>
    ///     Informação de erro detalhada (quando Success = false)
    /// </summary>
    [Key(3)]
    public ProviderErrorResponse? Error { get; set; }

    /// <summary>
    ///     Metadados essenciais da operação para rastreabilidade
    /// </summary>
    [Key(4)]
    public OperationMetadata Metadata { get; set; } = new();

    /// <summary>
    ///     Tipo de resposta: Síncrona (resultado imediato) ou Assíncrona (callback posterior)
    /// </summary>
    [Key(5)]
    public ResponseType ResponseType { get; set; } = ResponseType.Sync;
}