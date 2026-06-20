using Aedis.Http.Abstractions;
using global::Flurl.Http;

namespace Aedis.Http.Flurl;

/// <summary>
///     Fábrica de <see cref="IAedisHttpClient" /> sobre Flurl: materializa o <see cref="HttpClientProfile" />
///     num <c>HttpClient</c> (base, timeout, mTLS) e o envolve em um <see cref="FlurlClient" />. Registre via
///     <c>AddAedisHttpFlurl</c> para usar o Flurl no lugar do provider nativo.
/// </summary>
public sealed class FlurlHttpClientFactory : IAedisHttpClientFactory
{
    /// <inheritdoc />
    public IAedisHttpClient Create(HttpClientProfile profile) => new FlurlHttpClient(new FlurlClient(profile.CreateHttpClient()));
}
