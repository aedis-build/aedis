using Aedis.Http.Abstractions;

namespace Aedis.Http.Native;

/// <summary>
///     Implementação de <see cref="IAedisHttpClient" /> sobre o <c>HttpClient</c> nativo do .NET. Traduz a
///     <see cref="AedisHttpRequest" /> agnóstica em <c>HttpRequestMessage</c>, materializa a resposta em
///     bytes e nunca lança por status de erro HTTP (o status é devolvido para o chamador decidir).
/// </summary>
public sealed class NativeHttpClient : IAedisHttpClient
{
    private readonly HttpClient _httpClient;

    /// <summary>Cria o cliente envolvendo um <c>HttpClient</c> já configurado (base, timeout, certificados).</summary>
    public NativeHttpClient(HttpClient httpClient) => _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<AedisHttpResponse> SendAsync(AedisHttpRequest request, CancellationToken cancellationToken = default) {
        using var message = new HttpRequestMessage(request.Method, request.Url);

        if (request.Content is not null)
            message.Content = request.Content.ToHttpContent();

        foreach (var (name, value) in request.Headers)
            message.Headers.TryAddWithoutValidation(name, value);

        using var timeoutScope = CreateTimeoutScope(request.Timeout, cancellationToken);
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutScope?.Token ?? cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new AedisHttpResponse {
            StatusCode = (int)response.StatusCode,
            Headers = CollectHeaders(response),
            Body = body
        };
    }

    private static CancellationTokenSource? CreateTimeoutScope(TimeSpan? timeout, CancellationToken cancellationToken) {
        if (timeout is null)
            return null;

        var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scope.CancelAfter(timeout.Value);
        return scope;
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
