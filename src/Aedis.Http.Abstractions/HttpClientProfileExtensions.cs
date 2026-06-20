using System.Net;
using System.Net.Sockets;
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

        if (profile.Ssrf.Enabled)
            handler.ConnectCallback = CreateSsrfGuardedConnect(profile.Ssrf);

        var httpClient = new HttpClient(handler, disposeHandler: true) {
            Timeout = profile.Timeout
        };

        if (!string.IsNullOrWhiteSpace(profile.BaseAddress))
            httpClient.BaseAddress = new Uri(profile.BaseAddress);

        foreach (var (name, value) in profile.DefaultHeaders)
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);

        return httpClient;
    }

    private static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateSsrfGuardedConnect(SsrfPolicy policy) =>
        async (context, cancellationToken) => {
            var host = context.DnsEndPoint.Host;
            if (policy.IsHostBlocked(host))
                throw new SsrfBlockedException(host, null);

            var addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);

            var permitted = policy.IsHostAllowlisted(host)
                ? addresses
                : addresses.Where(address => !policy.IsAddressBlocked(address)).ToArray();

            if (permitted.Length == 0)
                throw new SsrfBlockedException(host, addresses.Length > 0 ? addresses[0] : null);

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try {
                await socket.ConnectAsync(permitted, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch {
                socket.Dispose();
                throw;
            }
        };
}
