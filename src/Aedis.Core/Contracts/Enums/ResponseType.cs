namespace Aedis.Core.Enums;

/// <summary>
///     Tipo de resposta de uma operação
/// </summary>
public enum ResponseType
{
    /// <summary>
    ///     Resposta síncrona: resultado disponível imediatamente na resposta HTTP.
    ///     Exemplo: provedor que responde de forma imediata.
    /// </summary>
    Sync = 0,

    /// <summary>
    ///     Resposta assíncrona: resultado entregue posteriormente via callback, arquivo ou fila.
    ///     Exemplo: provedor que processa em background e notifica depois (callback, fila, arquivo).
    /// </summary>
    Async = 1
}