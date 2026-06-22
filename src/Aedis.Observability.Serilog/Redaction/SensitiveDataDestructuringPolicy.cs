using System.Collections.Concurrent;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Política de destruturação que ofusca os campos marcados com <see cref="SensitiveDataAttribute" /> quando
///     um objeto é logado com <c>{@obj}</c>. Cobre o caso em que o nome do campo não bate na heurística do
///     <see cref="RedactionEnricher" /> (ex.: um <c>Name</c> que é nome de pessoa). Tipos sem nenhum campo
///     marcado seguem a destruturação padrão do Serilog.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy {
    private static readonly ConcurrentDictionary<Type, Accessor[]?> Cache = new();

    private readonly RedactionOptions _options;
    private readonly Redactor _redactor;

    /// <summary>
    ///     Cria a política de destruturação sensível.
    /// </summary>
    /// <param name="options">Opções de ofuscação.</param>
    public SensitiveDataDestructuringPolicy(RedactionOptions options) {
        _options = options;
        _redactor = new Redactor(options);
    }

    /// <inheritdoc />
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result) {
        result = null!;
        if (!_options.Enabled) {
            return false;
        }

        var accessors = Cache.GetOrAdd(value.GetType(), BuildAccessors);
        if (accessors is null) {
            return false;
        }

        var properties = new List<LogEventProperty>(accessors.Length);
        foreach (var accessor in accessors) {
            var raw = accessor.Read(value);
            var propertyValue = accessor.Sensitive
                ? new ScalarValue(_redactor.Apply(raw?.ToString(), accessor.Strategy))
                : propertyValueFactory.CreatePropertyValue(raw, true);
            properties.Add(new LogEventProperty(accessor.Name, propertyValue));
        }

        result = new StructureValue(properties, value.GetType().Name);
        return true;
    }

    private static Accessor[]? BuildAccessors(Type type) {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.All(property => property.GetCustomAttribute<SensitiveDataAttribute>() is null)) {
            return null;
        }

        return properties.Select(property => {
            var attribute = property.GetCustomAttribute<SensitiveDataAttribute>();
            return new Accessor(property.Name, attribute is not null, attribute?.Strategy ?? RedactionStrategy.Inherit, property);
        }).ToArray();
    }

    private sealed class Accessor {
        private readonly PropertyInfo _property;

        public Accessor(string name, bool sensitive, RedactionStrategy strategy, PropertyInfo property) {
            Name = name;
            Sensitive = sensitive;
            Strategy = strategy;
            _property = property;
        }

        public string Name { get; }
        public bool Sensitive { get; }
        public RedactionStrategy Strategy { get; }

        public object? Read(object target) {
            try {
                return _property.GetValue(target);
            }
            catch {
                return null;
            }
        }
    }
}
