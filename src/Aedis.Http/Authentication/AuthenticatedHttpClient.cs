using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;

namespace Aedis.Http.Authentication;

/// <summary>
///     Decorador de <see cref="IAedisHttpClient" /> que torna toda chamada autenticada: obtém o token via
///     <see cref="ITokenProvider" />, anexa <c>Authorization: Bearer</c> e, ao receber <c>401</c>, invalida o
///     token, obtém um novo e reenvia a requisição uma vez. É o equivalente agnóstico do retry-on-401 que os
///     adapters de produção implementavam por provider — aqui funciona igual sobre o cliente nativo ou Flurl,
///     pois o corpo materializado pode ser reenviado sem reconstrução manual.
/// </summary>
public sealed class AuthenticatedHttpClient : IAedisHttpClient
{
    private const int Unauthorized = 401;

    private readonly IAedisHttpClient _inner;
    private readonly ITokenProvider _tokenProvider;

    /// <summary>Cria o decorador sobre um cliente de transporte e o provedor de token.</summary>
    public AuthenticatedHttpClient(IAedisHttpClient inner, ITokenProvider tokenProvider) {
        _inner = inner;
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<AedisHttpResponse> SendAsync(AedisHttpRequest request, CancellationToken cancellationToken = default) {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        var response = await _inner.SendAsync(WithBearer(request, token), cancellationToken);

        if (response.StatusCode != Unauthorized)
            return response;

        await _tokenProvider.InvalidateAsync(cancellationToken);
        token = await _tokenProvider.GetTokenAsync(cancellationToken);
        return await _inner.SendAsync(WithBearer(request, token), cancellationToken);
    }

    private static AedisHttpRequest WithBearer(AedisHttpRequest request, string token) {
        request.Headers["Authorization"] = $"Bearer {token}";
        return request;
    }
}
