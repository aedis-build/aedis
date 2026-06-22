using Serilog.Core;
using Serilog.Events;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Enriquecedor que ofusca dados sensíveis em cada evento <strong>antes</strong> de qualquer sink (Console,
///     OTLP). Percorre recursivamente as propriedades — objetos, dicionários e listas — e mascara aquelas cujo
///     nome bate em <see cref="RedactionOptions.SecretKeys" /> (máscara total) ou
///     <see cref="RedactionOptions.PiiKeys" /> (estratégia de PII). Não inspeciona texto livre da mensagem: a
///     boa prática é logar de forma estruturada (<c>{Campo}</c>/<c>{@Objeto}</c>), não interpolado.
/// </summary>
public sealed class RedactionEnricher : ILogEventEnricher {
    private readonly RedactionOptions _options;
    private readonly Redactor _redactor;

    /// <summary>
    ///     Cria o enriquecedor de ofuscação.
    /// </summary>
    /// <param name="options">Opções de ofuscação (campos sensíveis e estratégias).</param>
    public RedactionEnricher(RedactionOptions options) {
        _options = options;
        _redactor = new Redactor(options);
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
        if (!_options.Enabled) {
            return;
        }

        List<LogEventProperty>? updates = null;
        foreach (var property in logEvent.Properties) {
            var redacted = Redact(property.Key, property.Value);
            if (!ReferenceEquals(redacted, property.Value)) {
                (updates ??= []).Add(new LogEventProperty(property.Key, redacted));
            }
        }

        if (updates is null) {
            return;
        }

        foreach (var update in updates) {
            logEvent.AddOrUpdateProperty(update);
        }
    }

    private LogEventPropertyValue Redact(string name, LogEventPropertyValue value) {
        var classification = _redactor.Classify(name);
        if (classification != Redactor.Classification.None) {
            return new ScalarValue(MaskWhole(value, _redactor.StrategyFor(classification)));
        }

        return value switch {
            StructureValue structure => RedactStructure(structure),
            DictionaryValue dictionary => RedactDictionary(dictionary),
            SequenceValue sequence => RedactSequence(sequence),
            _ => value
        };
    }

    private string MaskWhole(LogEventPropertyValue value, RedactionStrategy strategy) {
        return value is ScalarValue { Value: { } raw }
            ? _redactor.Apply(raw.ToString(), strategy)
            : _options.Placeholder;
    }

    private LogEventPropertyValue RedactStructure(StructureValue structure) {
        var changed = false;
        var properties = new List<LogEventProperty>(structure.Properties.Count);
        foreach (var property in structure.Properties) {
            var redacted = Redact(property.Name, property.Value);
            changed |= !ReferenceEquals(redacted, property.Value);
            properties.Add(new LogEventProperty(property.Name, redacted));
        }

        return changed ? new StructureValue(properties, structure.TypeTag) : structure;
    }

    private LogEventPropertyValue RedactDictionary(DictionaryValue dictionary) {
        var changed = false;
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
        foreach (var element in dictionary.Elements) {
            var keyName = element.Key.Value?.ToString() ?? string.Empty;
            var redacted = Redact(keyName, element.Value);
            changed |= !ReferenceEquals(redacted, element.Value);
            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(element.Key, redacted));
        }

        return changed ? new DictionaryValue(elements) : dictionary;
    }

    private LogEventPropertyValue RedactSequence(SequenceValue sequence) {
        var changed = false;
        var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
        foreach (var element in sequence.Elements) {
            var redacted = Redact(string.Empty, element);
            changed |= !ReferenceEquals(redacted, element);
            elements.Add(redacted);
        }

        return changed ? new SequenceValue(elements) : sequence;
    }
}
