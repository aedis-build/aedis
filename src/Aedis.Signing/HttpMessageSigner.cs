using System.Buffers.Text;
using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Logging;
using NSign;
using NSign.Http;
using NSign.Signatures;

namespace Aedis.Signing;

/// <summary>
///     Assina um <see cref="HttpRequestMessage" /> conforme a RFC 9421: <c>Date</c> (se ausente),
///     <c>Content-Digest</c> sobre os bytes exatos do conteúdo, e <c>Signature-Input</c>/<c>Signature</c> com os
///     componentes configurados, <c>created</c>/<c>expires</c>/<c>tag</c>/<c>keyid</c>/<c>alg</c>. A montagem
///     da base e a serialização são da NSign; a criptografia é do <see cref="ISignatureProvider" /> injetado.
/// </summary>
public sealed class HttpMessageSigner : IHttpMessageSigner
{
    private readonly HttpFieldOptions _fieldOptions = new();
    private readonly ISigningKeyProvider _keyProvider;
    private readonly ILogger<HttpMessageSigner> _logger;
    private readonly DefaultMessageSigner _messageSigner;
    private readonly SigningOptions _options;
    private readonly SigningKeyState _state;
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o signer sobre o provider de chave, a costura de criptografia, as opções e o estado da chave.</summary>
    public HttpMessageSigner(ISigningKeyProvider keyProvider, ISignatureProvider signatureProvider, SigningOptions options,
        SigningKeyState state, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null) {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        ArgumentNullException.ThrowIfNull(signatureProvider);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<HttpMessageSigner>();
        _messageSigner = new DefaultMessageSigner(loggerFactory.CreateLogger<DefaultMessageSigner>(),
            new SignatureProviderSigner(keyProvider, signatureProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc />
    public async Task SignAsync(HttpRequestMessage request, HttpSigningOptions? overrides = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnabled) return;

        var http = overrides ?? _options.Http;
        var handle = await WaitForKeyAsync(cancellationToken);

        if (http.SetDateHeaderIfMissing && request.Headers.Date is null)
            request.Headers.Date = _timeProvider.GetUtcNow();

        if (request.Content is { } content) {
            await content.LoadIntoBufferAsync(cancellationToken);
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
            request.Headers.Remove(ContentDigest.HeaderName);
            request.Headers.TryAddWithoutValidation(ContentDigest.HeaderName, ContentDigest.Compute(bytes));
        }

        var now = _timeProvider.GetUtcNow();
        var signingOptions = new MessageSigningOptions {
            SignatureName = http.SignatureLabel,
            UseUpdateSignatureParams = true,
            SetParameters = p => {
                p.WithCreated(now).WithExpires(now.AddSeconds(http.ExpiresInSeconds)).WithTag(http.Tag);
                if (http.IncludeNonce) p.WithNonce(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
            }
        };

        foreach (var name in http.Components) {
            var component = SignatureComponents.Parse(name);
            if (http.OptionalComponents.Contains(name, StringComparer.OrdinalIgnoreCase))
                signingOptions.WithOptionalComponent(component);
            else
                signingOptions.WithMandatoryComponent(component);
        }

        var context = new HttpRequestMessageSigningContext(_logger, _fieldOptions, request, cancellationToken, signingOptions);

        try {
            await _messageSigner.SignMessageAsync(context);
        }
        catch (SigningKeyInvalidStateException ex) {
            _state.MarkDegraded(ex);
            throw;
        }

        _logger.LogDebug("Requisição para {Uri} assinada com a chave {KeyId} ({Label}, tag {Tag}).",
            request.RequestUri, handle.KeyId, http.SignatureLabel, http.Tag);
    }

    private async Task<SigningKeyHandle> WaitForKeyAsync(CancellationToken cancellationToken) {
        if (_keyProvider.TryGetKey(out var ready) && ready is not null) return ready;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.BootstrapTimeoutSeconds), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try {
            return await _keyProvider.GetKeyAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            throw new SigningException($"A chave de assinatura não foi resolvida em {_options.BootstrapTimeoutSeconds}s.");
        }
    }
}

/// <summary><c>Signing:Enabled = false</c>: nada é assinado. Explícito, para o consumidor poder decidir o que fazer.</summary>
public sealed class DisabledHttpMessageSigner : IHttpMessageSigner
{
    /// <summary>Instância compartilhada.</summary>
    public static readonly DisabledHttpMessageSigner Instance = new();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task SignAsync(HttpRequestMessage request, HttpSigningOptions? overrides = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
