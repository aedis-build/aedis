using Microsoft.CodeAnalysis;

namespace Aedis.Analyzers.Security;

/// <summary>
///     Descritores dos diagnósticos de segurança do Aedis (AEDIS01xx). Cada um cobre uma classe de ataque da
///     matriz de controles e cita o CWE. A severidade padrão é Warning; a solução com <c>-warnaserror</c> a trata
///     como erro, e um projeto ajusta por <c>.editorconfig</c> (<c>dotnet_diagnostic.AEDIS01xx.severity</c>).
/// </summary>
internal static class SecurityDescriptors
{
    private const string Category = "Aedis.Security";

    internal static readonly DiagnosticDescriptor RegexWithoutTimeout = new(
        id: "AEDIS0101",
        title: "Regex sem timeout",
        messageFormat: "'{0}' roda sem timeout: uma entrada maliciosa trava a thread por backtracking (ReDoS, CWE-1333). Passe um TimeSpan de matchTimeout, use RegexOptions.NonBacktracking ou [GeneratedRegex] com timeout.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Regex do .NET sem timeout pode levar tempo exponencial com entradas preparadas. O padrão global de timeout é infinito.");

    internal static readonly DiagnosticDescriptor InsecureRandom = new(
        id: "AEDIS0102",
        title: "System.Random gerando valor secreto",
        messageFormat: "System.Random usado em '{0}', que parece gerar um valor secreto: a sequência é previsível (CWE-338). Use System.Security.Cryptography.RandomNumberGenerator.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Random não é criptográfico. Token, código, senha, nonce, salt, chave ou PIN precisam de RandomNumberGenerator.");

    internal static readonly DiagnosticDescriptor NonConstantTimeComparison = new(
        id: "AEDIS0103",
        title: "Comparação de segredo sem tempo constante",
        messageFormat: "'{0}' compara um valor secreto com uma operação que para no primeiro byte diferente: o tempo de resposta vaza o valor (CWE-208). Use CryptographicOperations.FixedTimeEquals.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Assinatura, HMAC, hash, token e chave de API devem ser comparados em tempo constante.");

    internal static readonly DiagnosticDescriptor WeakCryptography = new(
        id: "AEDIS0104",
        title: "Algoritmo criptográfico fraco",
        messageFormat: "'{0}' é um algoritmo quebrado ou obsoleto (CWE-327). Use SHA-256 ou superior, AES-GCM e as chaves do Aedis.Signing.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "MD5, SHA-1, DES, 3DES, RC2 e o modo ECB não protegem integridade nem confidencialidade.");

    internal static readonly DiagnosticDescriptor UnsafeCode = new(
        id: "AEDIS0105",
        title: "Código unsafe",
        messageFormat: "'{0}' sai do código gerenciado verificado e reabre a classe de buffer overflow (CWE-119). O Aedis mantém zero código unsafe (controle 19 da matriz).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Impõe por compilação o controle de memória segura da matriz: nenhum bloco ou membro unsafe, logo nenhum ponteiro. stackalloc em Span<T> é verificado pelo runtime e continua permitido.");
}
