using System.Security.Cryptography;
using System.Text;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Aplica uma <see cref="RedactionStrategy" /> a um valor textual, segundo as <see cref="RedactionOptions" />.
///     Também classifica um nome de campo como segredo, PII ou não-sensível.
/// </summary>
internal sealed class Redactor {
    private readonly RedactionOptions _options;

    internal Redactor(RedactionOptions options) {
        _options = options;
    }

    internal enum Classification {
        None,
        Secret,
        Pii
    }

    internal Classification Classify(string name) {
        var normalized = RedactionOptions.Normalize(name);
        if (normalized.Length == 0) {
            return Classification.None;
        }

        if (_options.SecretKeys.Contains(normalized)) {
            return Classification.Secret;
        }

        return _options.PiiKeys.Contains(normalized) ? Classification.Pii : Classification.None;
    }

    internal RedactionStrategy StrategyFor(Classification classification) {
        return classification == Classification.Secret ? _options.SecretStrategy : _options.PiiStrategy;
    }

    internal string Apply(string? value, RedactionStrategy strategy) {
        if (strategy == RedactionStrategy.Inherit) {
            strategy = _options.PiiStrategy;
        }

        if (string.IsNullOrEmpty(value)) {
            return _options.Placeholder;
        }

        return strategy switch {
            RedactionStrategy.Partial => Partial(value),
            RedactionStrategy.Hash => Hash(value),
            _ => _options.Placeholder
        };
    }

    private string Partial(string value) {
        return value.Length <= _options.KeepLast
            ? _options.Placeholder
            : _options.Placeholder + value[^_options.KeepLast..];
    }

    private string Hash(string value) {
        if (string.IsNullOrEmpty(_options.HashKey)) {
            return _options.Placeholder;
        }

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HashKey), Encoding.UTF8.GetBytes(value));
        return "hash:" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
