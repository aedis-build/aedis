namespace Aedis.Signing.Abstractions;

/// <summary>Política de verificação — o espelho do que o signer produz.</summary>
public sealed class HttpSignatureVerificationOptions
{
    /// <summary>Só assinaturas com este <c>tag</c> são consideradas. Padrão <c>aedis</c>.</summary>
    public string Tag { get; set; } = "aedis";

    /// <summary>Componentes que a assinatura precisa cobrir. Componentes extras cobertos são aceitos.</summary>
    public List<string> RequiredComponents { get; set; } = ["@method", "@target-uri", "content-type", "content-digest"];

    /// <summary>Tolerância para <c>created</c> no futuro (relógio do emissor adiantado). Padrão 5 min.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Exige o parâmetro <c>expires</c> (o signer sempre manda). Padrão: sim.</summary>
    public bool RequireExpires { get; set; } = true;

    /// <summary>Idade máxima aceita a partir de <c>created</c>, além do <c>expires</c>. <c>null</c> = só <c>expires</c>.</summary>
    public TimeSpan? MaxSignatureAge { get; set; }

    /// <summary>Exige <c>Content-Digest</c> válido quando há corpo. Padrão: sim.</summary>
    public bool RequireContentDigest { get; set; } = true;
}

/// <summary>Motivo de rejeição de uma assinatura HTTP.</summary>
public enum HttpSignatureFailure
{
    /// <summary>Nenhuma assinatura com o <c>tag</c> esperado.</summary>
    MissingSignature,

    /// <summary><c>Signature-Input</c>/<c>Signature</c> não parseiam.</summary>
    MalformedInput,

    /// <summary><c>keyid</c> não está no JWKS.</summary>
    UnknownKey,

    /// <summary><c>Content-Digest</c> ausente ou diferente do corpo.</summary>
    DigestMismatch,

    /// <summary><c>expires</c> já passou (ou <c>created</c> velho demais).</summary>
    Expired,

    /// <summary><c>created</c> está no futuro além da tolerância.</summary>
    CreatedInFuture,

    /// <summary>Componente obrigatório não coberto pela assinatura ou ausente na mensagem.</summary>
    ComponentMissing,

    /// <summary>A assinatura não confere com a base reconstruída.</summary>
    SignatureMismatch,

    /// <summary><c>alg</c> diferente de <c>ecdsa-p256-sha256</c>.</summary>
    WrongAlgorithm
}

/// <summary>Resultado da verificação. <see cref="Detail" /> é para log e nunca contém o corpo.</summary>
/// <param name="IsValid">Verdadeiro quando ao menos uma assinatura com o tag esperado é válida.</param>
/// <param name="Failure">Motivo da rejeição, quando inválida.</param>
/// <param name="KeyId"><c>keyid</c> da assinatura avaliada.</param>
/// <param name="Created">Parâmetro <c>created</c> da assinatura avaliada.</param>
/// <param name="Expires">Parâmetro <c>expires</c> da assinatura avaliada.</param>
/// <param name="Detail">Descrição curta do motivo, para log.</param>
public sealed record HttpSignatureVerificationResult(
    bool IsValid,
    HttpSignatureFailure? Failure,
    string? KeyId,
    DateTimeOffset? Created,
    DateTimeOffset? Expires,
    string? Detail)
{
    /// <summary>Resultado válido.</summary>
    public static HttpSignatureVerificationResult Valid(string keyId, DateTimeOffset? created, DateTimeOffset? expires) =>
        new(true, null, keyId, created, expires, null);

    /// <summary>Resultado inválido com o motivo.</summary>
    public static HttpSignatureVerificationResult Invalid(HttpSignatureFailure failure, string? detail = null,
        string? keyId = null, DateTimeOffset? created = null, DateTimeOffset? expires = null) =>
        new(false, failure, keyId, created, expires, detail);
}

/// <summary>
///     Requisição recebida, achatada para verificação: método, URL absoluta como o cliente a chamou, headers e
///     corpo. Construída a partir do <c>HttpRequest</c> do ASP.NET Core (receptor) ou de um
///     <see cref="HttpRequestMessage" /> capturado (testes, handlers).
/// </summary>
/// <param name="Method">Método HTTP.</param>
/// <param name="TargetUri">URL absoluta exatamente como assinada pelo emissor.</param>
/// <param name="Headers">Headers na ordem em que chegaram (request e conteúdo).</param>
/// <param name="Body">Bytes do corpo.</param>
public sealed record SignedHttpRequest(
    string Method,
    Uri TargetUri,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    ReadOnlyMemory<byte> Body)
{
    /// <summary>Valores de um header (case-insensitive), na ordem em que aparecem.</summary>
    public IEnumerable<string> GetHeaderValues(string name) =>
        Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value);

    /// <summary>De um <see cref="HttpRequestMessage" /> (ex.: capturado por um <c>DelegatingHandler</c> de teste).</summary>
    public static async Task<SignedHttpRequest> FromHttpRequestMessageAsync(HttpRequestMessage request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
            throw new ArgumentException("A requisição precisa ter URI absoluta.", nameof(request));

        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v)))
            .ToList();
        var body = request.Content is null ? ReadOnlyMemory<byte>.Empty : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        return new SignedHttpRequest(request.Method.Method, request.RequestUri, headers, body);
    }
}
