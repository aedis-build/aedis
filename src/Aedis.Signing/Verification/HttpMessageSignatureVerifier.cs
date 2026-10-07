using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSign;
using NSign.Http;
using NSign.Signatures;

namespace Aedis.Signing.Verification;

/// <summary>
///     Verifica assinaturas RFC 9421 produzidas pelo <see cref="HttpMessageSigner" /> — para os testes do
///     emissor e para receptores .NET. Fluxo: <c>Content-Digest</c> contra o corpo → parse de
///     <c>Signature-Input</c>/<c>Signature</c> e montagem da base (primitivas da NSign) → política (tag,
///     componentes obrigatórios, <c>created</c>/<c>expires</c> com tolerância, pelo <see cref="TimeProvider" />
///     injetado) → ECDSA P-256 (<c>r || s</c>) com a chave pública do <c>keyid</c>. Nunca lança por assinatura
///     inválida: devolve o motivo.
/// </summary>
public sealed class HttpMessageSignatureVerifier(
    ISigningPublicKeyResolver keys,
    HttpSignatureVerificationOptions? options = null,
    TimeProvider? timeProvider = null,
    ILogger? logger = null)
{
    private static readonly SignatureVerificationOptions NoPolicy = new();
    private readonly HttpFieldOptions _fieldOptions = new();
    private readonly ISigningPublicKeyResolver _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly HttpSignatureVerificationOptions _options = options ?? new HttpSignatureVerificationOptions();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Verifica um <see cref="HttpRequestMessage" /> (ex.: capturado em teste).</summary>
    public async Task<HttpSignatureVerificationResult> VerifyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        await VerifyAsync(await SignedHttpRequest.FromHttpRequestMessageAsync(request, cancellationToken), cancellationToken);

    /// <summary>Verifica uma requisição recebida já achatada.</summary>
    public async Task<HttpSignatureVerificationResult> VerifyAsync(SignedHttpRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var digestHeader = request.GetHeaderValues(ContentDigest.HeaderName).FirstOrDefault();
        if (_options.RequireContentDigest && (request.Body.Length > 0 || digestHeader is not null)
            && !ContentDigest.Verify(digestHeader, request.Body.Span))
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.DigestMismatch,
                digestHeader is null ? "Content-Digest ausente" : "Content-Digest não confere com o corpo");

        var context = new SignedHttpRequestVerificationContext(_logger, _fieldOptions, request, NoPolicy, cancellationToken);

        List<SignatureContext> candidates;
        try {
            candidates = context.SignaturesForVerification
                .Where(s => string.Equals(SafeTag(s), _options.Tag, StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (IsParseError(ex)) {
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.MalformedInput, ex.Message);
        }

        if (candidates.Count == 0)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.MissingSignature, $"Nenhuma assinatura com tag '{_options.Tag}'");

        HttpSignatureVerificationResult? last = null;
        foreach (var candidate in candidates) {
            last = await VerifyOneAsync(context, candidate, cancellationToken);
            if (last.IsValid) return last;
        }

        return last!;
    }

    private async Task<HttpSignatureVerificationResult> VerifyOneAsync(MessageContext context, SignatureContext signature,
        CancellationToken cancellationToken) {
        SignatureParamsComponent p;
        try {
            p = signature.SignatureParams;
        }
        catch (Exception ex) when (IsParseError(ex)) {
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.MalformedInput, $"Assinatura '{signature.Name}': {ex.Message}");
        }

        var keyId = p.KeyId;
        var created = p.Created;
        var expires = p.Expires;

        if (created is null)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.MalformedInput, "o parâmetro created é obrigatório", keyId, created, expires);
        if (_options.RequireExpires && expires is null)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.MalformedInput, "o parâmetro expires é obrigatório", keyId, created, expires);

        foreach (var required in _options.RequiredComponents) {
            var component = SignatureComponents.Parse(required);
            if (!p.Components.Any(c => c.Equals(component)))
                return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.ComponentMissing,
                    $"A assinatura não cobre o componente obrigatório '{required}'", keyId, created, expires);
        }

        foreach (var component in p.Components)
            if (!context.HasSignatureComponent(component))
                return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.ComponentMissing,
                    $"O componente coberto '{component.OriginalIdentifier ?? component.ComponentName}' não está presente na requisição", keyId, created, expires);

        var now = _timeProvider.GetUtcNow();
        if (expires is { } exp && exp <= now)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.Expired, $"Assinatura expirada em {exp:O}", keyId, created, expires);
        if (_options.MaxSignatureAge is { } maxAge && created.Value + maxAge < now)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.Expired, $"Assinatura mais antiga que {maxAge}", keyId, created, expires);
        if (created.Value > now + _options.ClockSkew)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.CreatedInFuture,
                $"created {created.Value:O} está além da tolerância de relógio", keyId, created, expires);

        if (!string.Equals(p.Algorithm, SigningOptions.Algorithm, StringComparison.Ordinal))
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.WrongAlgorithm,
                $"alg '{p.Algorithm}' não é {SigningOptions.Algorithm}", keyId, created, expires);

        if (string.IsNullOrEmpty(keyId))
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.UnknownKey, "o parâmetro keyid é obrigatório", keyId, created, expires);

        using var publicKey = await _keys.ResolveAsync(keyId, cancellationToken);
        if (publicKey is null)
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.UnknownKey, $"keyid '{keyId}' não encontrado no JWKS", keyId, created, expires);

        ReadOnlyMemory<byte> input;
        try {
            input = context.GetSignatureInput(p, out _);
        }
        catch (Exception ex) when (IsParseError(ex)) {
            return HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.ComponentMissing, ex.Message, keyId, created, expires);
        }

        var valid = publicKey.VerifyData(input.Span, signature.Signature.Span, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return valid
            ? HttpSignatureVerificationResult.Valid(keyId, created, expires)
            : HttpSignatureVerificationResult.Invalid(HttpSignatureFailure.SignatureMismatch, "A assinatura não confere com a base reconstruída", keyId, created, expires);
    }

    private static string? SafeTag(SignatureContext signature) {
        try {
            return signature.SignatureParams.Tag;
        }
        catch (Exception ex) when (IsParseError(ex)) {
            return null;
        }
    }

    private static bool IsParseError(Exception ex) =>
        ex is SignatureInputException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException;
}
