using System.Security.Cryptography;

namespace Aedis.Signing.Abstractions;

/// <summary>
///     Resolve a chave de assinatura da aplicação. <see cref="EnsureKeyAsync" /> é o bootstrap (chamado pelo
///     hosted service na subida; idempotente); <see cref="GetKeyAsync" /> é o que os signers usam (aguarda o
///     bootstrap terminar).
/// </summary>
public interface ISigningKeyProvider
{
    /// <summary>Resolve (ou cria, conforme a configuração) a chave e a publica no estado compartilhado.</summary>
    Task<SigningKeyHandle> EnsureKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Chave já resolvida, sem esperar.</summary>
    bool TryGetKey(out SigningKeyHandle? handle);

    /// <summary>Aguarda a chave do bootstrap (ou a falha dele).</summary>
    Task<SigningKeyHandle> GetKeyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Costura de fornecedor da criptografia: recebe o digest SHA-256 da base de assinatura e devolve a
///     assinatura ECDSA P-256 em <c>r || s</c> (IEEE P1363). Em processo é <c>ECDsa.SignHash</c>; num HSM é
///     a chamada de assinatura do cofre com conversão de formato quando preciso. Nenhuma biblioteca de
///     assinatura HTTP chega até aqui.
/// </summary>
public interface ISignatureProvider
{
    /// <summary>Algoritmo RFC 9421 produzido (<see cref="SigningOptions.Algorithm" />).</summary>
    string Algorithm { get; }

    /// <summary>Assina o <paramref name="digest" /> (32 bytes, SHA-256) e devolve <c>r || s</c> com 64 bytes.</summary>
    Task<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, CancellationToken cancellationToken = default);
}

/// <summary>
///     Assina uma requisição HTTP conforme a RFC 9421 (HTTP Message Signatures): acrescenta
///     <c>Content-Digest</c> (RFC 9530), <c>Date</c> se ausente, <c>Signature-Input</c> e <c>Signature</c>.
///     Deve ser chamado com a requisição <strong>final</strong> (URL absoluta, headers de autenticação já
///     aplicados, conteúdo definido) — tipicamente por um <c>DelegatingHandler</c> no fim do pipeline.
/// </summary>
public interface IHttpMessageSigner
{
    /// <summary><c>false</c> quando <c>Signing:Enabled = false</c>: <see cref="SignAsync" /> não faz nada.</summary>
    bool IsEnabled { get; }

    /// <summary>
    ///     Assina <paramref name="request" /> in-place. Erros permanentes lançam <see cref="SigningException" />;
    ///     transitórios do provider lançam <c>ServiceTemporarilyUnavailableException</c>.
    /// </summary>
    Task SignAsync(HttpRequestMessage request, HttpSigningOptions? overrides = null, CancellationToken cancellationToken = default);
}

/// <summary>Documento JWKS com a(s) chave(s) pública(s) de assinatura da aplicação.</summary>
public interface IJwksProvider
{
    /// <summary>JWKS já disponível (chave resolvida), sem esperar.</summary>
    bool TryGet(out JsonWebKeySetDocument? document);

    /// <summary>Aguarda o bootstrap da chave e devolve o JWKS.</summary>
    Task<JsonWebKeySetDocument> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolve a chave pública (ECDSA P-256) de um <c>keyid</c>. <c>null</c> = desconhecida.</summary>
public interface ISigningPublicKeyResolver
{
    /// <summary>Devolve uma instância <see cref="ECDsa" /> só com a chave pública do <paramref name="keyId" />, ou <c>null</c>.</summary>
    Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Sondagem opcional do provider pelo health check (<c>Signing:HealthCheck:ProbeProvider</c>): confirma
///     no cofre que a chave resolvida continua utilizável, sem jamais listar chaves.
/// </summary>
public interface ISigningKeyProbe
{
    /// <summary>Consulta o provider sobre a chave <paramref name="handle" />.</summary>
    Task<SigningKeyProbeResult> ProbeAsync(SigningKeyHandle handle, CancellationToken cancellationToken = default);
}

/// <summary>Resultado da sondagem do provider.</summary>
/// <param name="IsUsable">Verdadeiro quando a chave está habilitada e serve para assinar.</param>
/// <param name="Description">Estado reportado pelo provider, para o health check.</param>
public sealed record SigningKeyProbeResult(bool IsUsable, string Description);
