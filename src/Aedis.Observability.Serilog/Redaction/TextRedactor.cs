using System.Text.RegularExpressions;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Mascara segredos embutidos em texto livre, onde a classificação por nome de campo não alcança: cabeçalhos
///     <c>Bearer</c>/<c>Basic</c>, pares chave/valor de senha, token e api-key, blocos PEM de chave privada e
///     access keys de nuvem. Só os padrões genéricos de segredo são tratados — dados pessoais continuam a cargo
///     da classificação por nome. Um pré-filtro barato evita rodar as expressões em textos sem candidato.
/// </summary>
internal sealed partial class TextRedactor
{
    private static readonly char[] CandidateMarkers = ['=', ':'];
    private readonly string _placeholder;

    internal TextRedactor(RedactionOptions options) {
        _placeholder = options.Placeholder;
    }

    /// <summary>Devolve o texto com os segredos substituídos; a mesma instância quando nada mudou.</summary>
    internal string Redact(string text) {
        if (string.IsNullOrEmpty(text) || !MightContainSecret(text)) {
            return text;
        }

        var result = PrivateKeyBlock().Replace(text, _placeholder);
        result = AuthorizationScheme().Replace(result, match => match.Groups["scheme"].Value + " " + _placeholder);
        result = KeyValueSecret().Replace(result, match => match.Groups["key"].Value + match.Groups["sep"].Value + _placeholder);
        result = CloudAccessKeyId().Replace(result, _placeholder);

        return result == text ? text : result;
    }

    private static bool MightContainSecret(string text) {
        return text.IndexOfAny(CandidateMarkers) >= 0
               || text.Contains("Bearer", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Basic", StringComparison.OrdinalIgnoreCase)
               || text.Contains("BEGIN", StringComparison.Ordinal)
               || text.Contains("AKIA", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\b(?<scheme>Bearer|Basic)\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationScheme();

    [GeneratedRegex(@"(?<key>\b(?:password|passwd|pwd|secret|client[_-]?secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key|x[_-]?api[_-]?key)\b)(?<sep>\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CloudAccessKeyId();
}
