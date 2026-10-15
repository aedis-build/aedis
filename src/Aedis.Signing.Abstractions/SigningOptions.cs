using System.ComponentModel.DataAnnotations;

namespace Aedis.Signing.Abstractions;

/// <summary>
///     Configuração do módulo de assinatura (seção <c>Signing</c>): assinaturas HTTP (RFC 9421) com uma
///     chave ECDSA P-256 cuja parte privada vive num provider — em processo (dev/testes) ou num cofre/HSM
///     registrado por um pacote de provider — e cuja parte pública é publicada como JWKS. O módulo é genérico;
///     o parâmetro <c>tag</c> da assinatura identifica o uso (ex.: webhooks).
/// </summary>
public sealed class SigningOptions
{
    /// <summary>Nome da seção de configuração (<c>Signing</c>).</summary>
    public const string SectionName = "Signing";

    /// <summary>Algoritmo RFC 9421 produzido por este módulo (ECDSA P-256 + SHA-256, assinatura <c>r || s</c>).</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>Desligado: <c>IHttpMessageSigner.IsEnabled == false</c>, sem bootstrap, health check nem JWKS.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Nome do provider da chave. <see cref="SigningKeyProviders.InProcess" /> é o único embutido; pacotes de
    ///     provider registram outros (ex.: <c>AwsKms</c>) e podem fixá-lo pelo <c>SigningBuilder</c>.
    /// </summary>
    [Required]
    public string Provider { get; set; } = SigningKeyProviders.InProcess;

    /// <summary>
    ///     Nome da aplicação usado por providers para nomear/etiquetar a chave. Padrão: derivado do nome da
    ///     aplicação sem sufixo de host (<c>loja.pedidos.api</c> → <c>loja-pedidos</c>), para API e worker do
    ///     mesmo serviço compartilharem a chave.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>Quanto tempo o bootstrap insiste em erro transitório do provider antes de abortar o host. Padrão 60 s.</summary>
    [Range(5, 600)]
    public int BootstrapTimeoutSeconds { get; set; } = 60;

    /// <summary>Chave em processo (dev/testes).</summary>
    public InProcessKeyOptions InProcess { get; set; } = new();

    /// <summary>Como a assinatura HTTP é montada.</summary>
    public HttpSigningOptions Http { get; set; } = new();

    /// <summary>Publicação da chave pública.</summary>
    public JwksOptions Jwks { get; set; } = new();

    /// <summary>Health check <c>signing</c> (tag <c>ready</c>).</summary>
    public SigningHealthCheckOptions HealthCheck { get; set; } = new();
}

/// <summary>Nomes de provider de chave conhecidos pelo núcleo.</summary>
public static class SigningKeyProviders
{
    /// <summary>Chave ECDSA em processo: PEM configurado ou efêmera. Só dev/testes.</summary>
    public const string InProcess = "InProcess";
}

/// <summary>Chave em processo — só dev/testes. Nunca em produção.</summary>
public sealed class InProcessKeyOptions
{
    /// <summary>Chave privada ECDSA P-256 em PEM (<c>PRIVATE KEY</c> PKCS#8 ou <c>EC PRIVATE KEY</c>).</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Sem PEM, gera uma chave efêmera na subida (loga Warning). <c>kid</c> = <c>dev-{thumbprint}</c>.</summary>
    public bool AllowEphemeralKey { get; set; }

    /// <summary><c>kid</c> a publicar no JWKS. Padrão: thumbprint SHA-256 da chave pública.</summary>
    public string? KeyId { get; set; }
}

/// <summary>Como a assinatura HTTP é montada.</summary>
public sealed class HttpSigningOptions
{
    /// <summary>Label da assinatura em <c>Signature-Input</c>/<c>Signature</c>. Padrão <c>sig1</c>.</summary>
    [Required]
    public string SignatureLabel { get; set; } = "sig1";

    /// <summary>Parâmetro <c>tag</c> (RFC 9421 §2.3): identifica o uso. Cada consumidor define o seu (ex.: <c>loja-webhook</c>).</summary>
    [Required]
    public string Tag { get; set; } = "aedis";

    /// <summary>
    ///     Componentes cobertos, na ordem. Derivados começam com <c>@</c>; os demais são headers. Os listados em
    ///     <see cref="OptionalComponents" /> entram só se presentes; os outros são obrigatórios.
    /// </summary>
    public List<string> Components { get; set; } = [
        "@method", "@target-uri", "content-type", "content-digest", "date", "idempotency-key"
    ];

    /// <summary>Headers que entram na assinatura apenas quando presentes na requisição.</summary>
    public List<string> OptionalComponents { get; set; } = ["date", "idempotency-key"];

    /// <summary><c>expires = created + N</c>. Janela de replay do receptor. Padrão 300 s.</summary>
    [Range(30, 3600)]
    public int ExpiresInSeconds { get; set; } = 300;

    /// <summary>Acrescenta um <c>nonce</c> aleatório (16 bytes, base64url). Padrão: não (<c>Idempotency-Key</c> já cobre).</summary>
    public bool IncludeNonce { get; set; }

    /// <summary>Define o header <c>Date</c> (RFC 1123) quando ausente, para ele poder ser coberto. Padrão: sim.</summary>
    public bool SetDateHeaderIfMissing { get; set; } = true;
}

/// <summary>Publicação da chave pública.</summary>
public sealed class JwksOptions
{
    /// <summary>Path padrão da rota JWKS quando a aplicação não informa um.</summary>
    [Required]
    public string Path { get; set; } = "/.well-known/jwks.json";

    /// <summary><c>Cache-Control: public, max-age</c> da resposta JWKS. Padrão 3600.</summary>
    [Range(0, 86400)]
    public int CacheSeconds { get; set; } = 3600;
}

/// <summary>Health check <c>signing</c> (tag <c>ready</c>).</summary>
public sealed class SigningHealthCheckOptions
{
    /// <summary>Liga o health check. Padrão: ligado.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Com <c>true</c>, cada probe consulta o provider (<c>ISigningKeyProbe</c>) e exige a chave utilizável;
    ///     padrão: só o estado em memória.
    /// </summary>
    public bool ProbeProvider { get; set; }
}
