namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Fornece o token de acesso de uma integração, cuidando de obtê-lo, cacheá-lo e renová-lo quando expira.
///     Abstrai o esquema concreto (OAuth2 client_credentials etc.) e a origem do armazenamento (memória ou
///     cache distribuído). O cliente autenticado usa <see cref="GetTokenAsync" /> antes de cada chamada e
///     <see cref="InvalidateAsync" /> ao receber 401 para forçar a renovação.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    ///     Devolve um token de acesso válido — do cache quando presente, ou obtendo um novo de forma segura
    ///     contra concorrência (single-flight: chamadas paralelas em cache frio resultam em uma única busca).
    /// </summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Invalida o token cacheado, forçando nova obtenção na próxima chamada (ex.: após um 401).</summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
