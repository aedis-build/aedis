namespace Aedis.Core.Errors;

/// <summary>
///     Categorias de erro para facilitar tratamento e retry logic
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    ///     Erro de validação (400, 422)
    /// </summary>
    Validation,

    /// <summary>
    ///     Erro de autenticação (401)
    /// </summary>
    Authentication,

    /// <summary>
    ///     Erro de autorização (403)
    /// </summary>
    Authorization,

    /// <summary>
    ///     Recurso não encontrado (404)
    /// </summary>
    NotFound,

    /// <summary>
    ///     Conflito de estado (409)
    /// </summary>
    Conflict,

    /// <summary>
    ///     Rate limit excedido (429)
    /// </summary>
    RateLimit,

    /// <summary>
    ///     Erro interno do provedor (500+)
    /// </summary>
    ProviderError,

    /// <summary>
    ///     Erro de timeout/conectividade
    /// </summary>
    NetworkError,

    /// <summary>
    ///     Erro desconhecido
    /// </summary>
    Unknown
}