using Aedis.Signing.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Aedis.Signing.AspNetCore;

/// <summary>Leitura de uma requisição ASP.NET Core recebida no formato que o verificador consome.</summary>
public static class SignedHttpRequestAspNetCoreExtensions
{
    /// <summary>
    ///     Achata o <see cref="HttpRequest" /> em <see cref="SignedHttpRequest" />. Lê o corpo com
    ///     <c>EnableBuffering</c> e rebobina, para o handler seguinte continuar lendo. A URL é reconstruída do que
    ///     chegou (scheme/host/path/query) — atrás de proxy, configure <c>UseForwardedHeaders</c> antes, para
    ///     bater com a URL que o emissor assinou.
    /// </summary>
    public static async Task<SignedHttpRequest> ToSignedHttpRequestAsync(this HttpRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;

        var uri = new Uri($"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}{request.Path.ToUriComponent()}{request.QueryString.ToUriComponent()}");
        var headers = request.Headers
            .SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v ?? string.Empty)))
            .ToList();

        return new SignedHttpRequest(request.Method, uri, headers, buffer.ToArray());
    }
}
