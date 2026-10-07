using NSign.Signatures;

namespace Aedis.Signing;

/// <summary>
///     Mapeia os nomes de componente da configuração para os objetos da NSign e calcula os componentes
///     derivados (RFC 9421 §2.2) a partir de método + URI absoluta — compartilhado por assinatura e
///     verificação, para os dois lados construírem a mesma base.
/// </summary>
internal static class SignatureComponents
{
    internal static SignatureComponent Parse(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().ToLowerInvariant();

        return normalized switch {
            "@method" => SignatureComponent.Method,
            "@target-uri" => SignatureComponent.RequestTargetUri,
            "@authority" => SignatureComponent.Authority,
            "@scheme" => SignatureComponent.Scheme,
            "@request-target" => SignatureComponent.RequestTarget,
            "@path" => SignatureComponent.Path,
            "@query" => SignatureComponent.Query,
            "content-type" => SignatureComponent.ContentType,
            "content-digest" => SignatureComponent.ContentDigest,
            "content-length" => SignatureComponent.ContentLength,
            _ when normalized.StartsWith('@') => throw new NotSupportedException($"Componente derivado '{name}' não é suportado na assinatura de requisições."),
            _ => new HttpHeaderComponent(normalized)
        };
    }

    internal static string? DerivedValue(DerivedComponent component, string method, Uri targetUri) {
        ArgumentNullException.ThrowIfNull(component);
        if (!targetUri.IsAbsoluteUri) throw new ArgumentException("A URI alvo precisa ser absoluta.", nameof(targetUri));

        return component.ComponentName switch {
            "@method" => method.ToUpperInvariant(),
            "@target-uri" => targetUri.AbsoluteUri,
            "@authority" => targetUri.IsDefaultPort
                ? targetUri.Host.ToLowerInvariant()
                : $"{targetUri.Host.ToLowerInvariant()}:{targetUri.Port}",
            "@scheme" => targetUri.Scheme.ToLowerInvariant(),
            "@request-target" => targetUri.PathAndQuery,
            "@path" => string.IsNullOrEmpty(targetUri.AbsolutePath) ? "/" : targetUri.AbsolutePath,
            "@query" => string.IsNullOrEmpty(targetUri.Query) ? "?" : targetUri.Query,
            _ => null
        };
    }

    internal static IEnumerable<string> QueryParamValues(Uri targetUri, string name) {
        var query = targetUri.Query;
        if (string.IsNullOrEmpty(query)) yield break;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            if (!string.Equals(key, name, StringComparison.Ordinal)) continue;
            yield return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }
    }
}
