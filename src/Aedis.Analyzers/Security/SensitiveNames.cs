using System;
using System.Text.RegularExpressions;

namespace Aedis.Analyzers.Security;

/// <summary>
///     Reconhece pelo nome um símbolo que guarda ou produz um valor secreto. É uma heurística: o nome é o único
///     sinal que o compilador dá sobre a intenção, por isso os diagnósticos dizem "parece".
/// </summary>
internal static class SensitiveNames
{
    // the analyzer must never be its own ReDoS: fixed timeout on both patterns
    private static readonly Regex Generated = new(
        "(token|secret|password|passwd|pwd|nonce|otp|salt|apikey|api_key|pin|key|code)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex Compared = new(
        "(signature|hmac|mac|digest|hash|token|secret|apikey|api_key|password)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    internal static bool LooksGenerated(string? name) => !string.IsNullOrEmpty(name) && Generated.IsMatch(name);

    internal static bool LooksCompared(string? name) => !string.IsNullOrEmpty(name) && Compared.IsMatch(name);
}
