using System.Text;
using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Authentication;

/// <summary>
///     Estratégia OAuth2 <c>client_credentials</c> que apresenta as credenciais via cabeçalho HTTP Basic
///     (usuário/senha = client_id/client_secret), enviando apenas <c>grant_type</c> no corpo. Modelo comum
///     em provedores que esperam Basic Auth no endpoint de token.
/// </summary>
public sealed class BasicAuthenticationStrategy(string username, string password) : IAuthenticationStrategy
{
    /// <inheritdoc />
    public AedisHttpRequest BuildTokenRequest(string tokenEndpoint) {
        var request = AedisHttpRequest.Post(tokenEndpoint,
            AedisHttpContent.Form([new KeyValuePair<string, string>("grant_type", "client_credentials")]));

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers["Authorization"] = $"Basic {credentials}";

        return request;
    }
}

/// <summary>
///     Estratégia OAuth2 <c>client_credentials</c> que envia <c>client_id</c> e <c>client_secret</c> no corpo
///     url-encoded (junto de <c>grant_type</c>). Combine com um <see cref="HttpClientProfile" /> com
///     certificado de cliente para o modelo "client_credentials sobre mTLS".
/// </summary>
public sealed class ClientCredentialsBodyStrategy(string clientId, string clientSecret) : IAuthenticationStrategy
{
    /// <inheritdoc />
    public AedisHttpRequest BuildTokenRequest(string tokenEndpoint) =>
        AedisHttpRequest.Post(tokenEndpoint, AedisHttpContent.Form([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        ]));
}
