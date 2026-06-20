namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Estratégia de apresentação de credencial ao obter um token: monta a requisição ao endpoint de token
///     conforme o esquema do provider. É o ponto de variação entre integrações que usam o mesmo fluxo OAuth2
///     <c>client_credentials</c> mas apresentam a credencial de formas diferentes — ex.: credenciais no
///     corpo url-encoded, ou via cabeçalho HTTP Basic. O transporte (mTLS) é ortogonal e vive no
///     <see cref="HttpClientProfile" />.
/// </summary>
public interface IAuthenticationStrategy
{
    /// <summary>
    ///     Monta a requisição POST ao <paramref name="tokenEndpoint" /> que obtém o token, já com a credencial
    ///     apresentada conforme o esquema (header Basic, campos no corpo, etc.).
    /// </summary>
    AedisHttpRequest BuildTokenRequest(string tokenEndpoint);
}
