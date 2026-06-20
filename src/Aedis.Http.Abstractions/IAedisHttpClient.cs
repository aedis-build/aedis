namespace Aedis.Http.Abstractions;

/// <summary>
///     Cliente HTTP agnóstico de provider: envia uma <see cref="AedisHttpRequest" /> e devolve uma
///     <see cref="AedisHttpResponse" /> materializada. Implementado por providers concretos (nativo
///     <c>System.Net.Http</c>, Flurl) sem que o código de integração saiba qual está em uso.
/// </summary>
public interface IAedisHttpClient
{
    /// <summary>Envia a requisição e devolve a resposta com o corpo já lido. Não lança por status de erro HTTP.</summary>
    Task<AedisHttpResponse> SendAsync(AedisHttpRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
///     Fábrica de <see cref="IAedisHttpClient" /> a partir de um <see cref="HttpClientProfile" /> (base,
///     timeout, certificados mTLS, headers). O provider concreto cuida do ciclo de vida e do reuso eficiente
///     das conexões — o chamador deve reter o cliente criado (ex.: como singleton) em vez de recriá-lo por
///     requisição.
/// </summary>
public interface IAedisHttpClientFactory
{
    /// <summary>Cria (ou reaproveita) um cliente HTTP configurado conforme o <paramref name="profile" />.</summary>
    IAedisHttpClient Create(HttpClientProfile profile);
}
