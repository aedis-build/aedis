namespace Aedis.Observability.Serilog;

/// <summary>
///     Ofusca os valores de parâmetros sensíveis em uma query string, preservando os demais. Complementa o
///     <see cref="RedactionEnricher" /> (que age por nome de propriedade estruturada) para o caso específico de
///     uma query string logada como texto — por exemplo, no access-log de requisições, onde um
///     <c>?access_token=…</c> vazaria. Usa as mesmas chaves de <see cref="RedactionOptions" /> (segredos e PII),
///     sempre com máscara total no valor.
/// </summary>
public static class QueryStringRedactor {
    /// <summary>
    ///     Retorna a query string com os valores de parâmetros sensíveis mascarados. Aceita com ou sem o
    ///     <c>?</c> inicial; devolve vazio para entrada vazia.
    /// </summary>
    /// <param name="queryString">Query string original (ex.: <c>?access_token=abc&amp;page=2</c>).</param>
    /// <param name="options">Opções de ofuscação (chaves sensíveis e placeholder).</param>
    public static string Redact(string? queryString, RedactionOptions options) {
        if (string.IsNullOrEmpty(queryString)) {
            return string.Empty;
        }

        var hasPrefix = queryString[0] == '?';
        var body = hasPrefix ? queryString[1..] : queryString;
        if (body.Length == 0) {
            return queryString;
        }

        var parts = body.Split('&');
        for (var i = 0; i < parts.Length; i++) {
            var separator = parts[i].IndexOf('=');
            if (separator < 0) {
                continue;
            }

            var name = parts[i][..separator];
            var normalized = RedactionOptions.Normalize(Uri.UnescapeDataString(name));
            if (options.SecretKeys.Contains(normalized) || options.PiiKeys.Contains(normalized)) {
                parts[i] = name + "=" + options.Placeholder;
            }
        }

        var redacted = string.Join('&', parts);
        return hasPrefix ? "?" + redacted : redacted;
    }
}
