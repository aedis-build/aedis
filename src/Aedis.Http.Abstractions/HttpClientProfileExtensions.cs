using System.Security.Cryptography.X509Certificates;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Materializa um <see cref="HttpClientProfile" /> num <c>HttpClient</c> da BCL — com
///     <c>SocketsHttpHandler</c> (reciclagem de conexão + certificados mTLS), endereço base, timeout e
///     headers padrão. Compartilhado pelos providers (nativo e Flurl) para que a construção do transporte
///     (incluindo mTLS) viva em um único lugar.
/// </summary>
public static class HttpClientProfileExtensions
{
    /// <summary>Cria um <c>HttpClient</c> configurado conforme o perfil. O chamador é dono do ciclo de vida do cliente.</summary>
    public static HttpClient CreateHttpClient(this HttpClientProfile profile) {
        var handler = new SocketsHttpHandler {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        if (profile.ClientCertificates.Count > 0) {
            handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
            foreach (var certificate in profile.ClientCertificates)
                handler.SslOptions.ClientCertificates.Add(certificate);
        }

        var httpClient = new HttpClient(handler, disposeHandler: true) {
            Timeout = profile.Timeout
        };

        if (!string.IsNullOrWhiteSpace(profile.BaseAddress))
            httpClient.BaseAddress = new Uri(profile.BaseAddress);

        foreach (var (name, value) in profile.DefaultHeaders)
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);

        return httpClient;
    }
}
