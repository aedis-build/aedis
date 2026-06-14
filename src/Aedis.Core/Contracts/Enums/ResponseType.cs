namespace Aedis.Core.Enums;

/// <summary>
///     Tipo de resposta de uma operação
/// </summary>
public enum ResponseType
{
    /// <summary>
    ///     Resposta síncrona: resultado disponível imediatamente na resposta HTTP.
    ///     Exemplo: CERC sem interoperabilidade.
    /// </summary>
    Sync = 0,

    /// <summary>
    ///     Resposta assíncrona: resultado entregue posteriormente via callback, arquivo ou fila.
    ///     Exemplos: B3 (sempre async), CERC com interop, Nuclea (IBM MQ).
    /// </summary>
    Async = 1
}