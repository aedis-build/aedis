using Aedis.Http.Abstractions;

namespace Aedis.Http.Native;

/// <summary>
///     Fábrica default de <see cref="IAedisHttpClient" />, sobre o <c>HttpClient</c> nativo. Materializa o
///     <see cref="HttpClientProfile" /> (base, timeout, headers e certificados mTLS) e devolve um cliente
///     pronto. O chamador deve reter o cliente criado (ex.: singleton), evitando o anti-padrão de recriá-lo
///     por requisição.
/// </summary>
public sealed class NativeHttpClientFactory : IAedisHttpClientFactory
{
    /// <inheritdoc />
    public IAedisHttpClient Create(HttpClientProfile profile) => new NativeHttpClient(profile.CreateHttpClient());
}
