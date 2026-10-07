using Aedis.Signing.Abstractions;

namespace Aedis.Signing;

/// <summary>
///     <see cref="DelegatingHandler" /> que assina toda requisição de saída com o <see cref="IHttpMessageSigner" />.
///     Coloque-o como o handler mais interno (logo antes do transporte), para a assinatura cobrir a URL e os
///     headers finais — inclusive os de autenticação aplicados por handlers anteriores. Funciona com qualquer
///     <see cref="HttpClient" />, nativo ou de biblioteca. Com a assinatura desligada, é transparente.
/// </summary>
public sealed class HttpSignatureHandler : DelegatingHandler
{
    private readonly HttpSigningOptions? _overrides;
    private readonly IHttpMessageSigner _signer;

    /// <summary>Cria o handler com o signer e, opcionalmente, ajustes por cliente (tag, label, componentes).</summary>
    public HttpSignatureHandler(IHttpMessageSigner signer, HttpSigningOptions? overrides = null) {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _overrides = overrides;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        if (_signer.IsEnabled)
            await _signer.SignAsync(request, _overrides, cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
