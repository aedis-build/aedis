using Aedis.Http.Abstractions;
using global::Flurl.Http;

namespace Aedis.Http.Flurl;

/// <summary>
///     Implementação de <see cref="IAedisHttpClient" /> sobre Flurl.Http. Traduz a
///     <see cref="AedisHttpRequest" /> agnóstica em uma requisição Flurl, permite qualquer status (o status é
///     devolvido ao chamador em vez de lançar) e materializa o corpo em bytes.
/// </summary>
public sealed class FlurlHttpClient : IAedisHttpClient
{
    private readonly IFlurlClient _flurlClient;

    /// <summary>Cria o cliente envolvendo um <see cref="IFlurlClient" /> já configurado (base, timeout, certificados).</summary>
    public FlurlHttpClient(IFlurlClient flurlClient) => _flurlClient = flurlClient;

    /// <inheritdoc />
    public async Task<AedisHttpResponse> SendAsync(AedisHttpRequest request, CancellationToken cancellationToken = default) {
        var flurlRequest = _flurlClient.Request(request.Url).AllowAnyHttpStatus();

        foreach (var (name, value) in request.Headers)
            flurlRequest = flurlRequest.WithHeader(name, value);

        if (request.Timeout is not null)
            flurlRequest = flurlRequest.WithTimeout(request.Timeout.Value);

        var content = request.Content?.ToHttpContent();
        using var response = await flurlRequest.SendAsync(request.Method, content, cancellationToken: cancellationToken);
        var body = await response.ResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken);

        return new AedisHttpResponse {
            StatusCode = response.StatusCode,
            Headers = CollectHeaders(response.ResponseMessage),
            Body = body
        };
    }

    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response) {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        foreach (var header in response.Content.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        return headers;
    }
}
