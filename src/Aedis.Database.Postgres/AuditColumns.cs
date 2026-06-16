using System.Collections.Concurrent;
using System.Reflection;
using Aedis.Security.Abstractions;

namespace Aedis.Database.Postgres;

/// <summary>
///     Metadados de auditoria por tipo de entidade (cacheados): localiza, por convenção, as propriedades
///     <c>CreatedAt</c>, <c>CreatedBy</c>, <c>UpdatedAt</c>, <c>UpdatedBy</c> e <c>UpdatedReason</c> e as
///     carimba a partir do <see cref="IAuditContext" /> — forçando o "quem/quando/porquê" apenas quando a
///     coluna existe na entidade. <c>CreatedAt</c>/<c>CreatedBy</c> só são preenchidos quando ainda vazios
///     (preserva a criação original em updates); <c>UpdatedAt</c>/<c>UpdatedBy</c> são sempre atualizados.
/// </summary>
internal sealed class AuditColumns
{
    private static readonly ConcurrentDictionary<Type, AuditColumns> Cache = new();

    private readonly PropertyInfo? _createdAt;
    private readonly PropertyInfo? _createdBy;
    private readonly PropertyInfo? _updatedAt;
    private readonly PropertyInfo? _updatedBy;
    private readonly PropertyInfo? _updatedReason;

    private AuditColumns(Type type) {
        PropertyInfo? Find(string name) =>
            type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is
                { CanWrite: true } p
                ? p
                : null;

        _createdAt = Find("CreatedAt");
        _createdBy = Find("CreatedBy");
        _updatedAt = Find("UpdatedAt");
        _updatedBy = Find("UpdatedBy");
        _updatedReason = Find("UpdatedReason");

        HasAny = _createdAt is not null || _createdBy is not null || _updatedAt is not null
                 || _updatedBy is not null || _updatedReason is not null;
    }

    public bool HasAny { get; }

    public static AuditColumns For(Type type) => Cache.GetOrAdd(type, t => new AuditColumns(t));

    public void Stamp(object entity, IAuditContext audit) {
        if (_createdAt is not null && IsUnsetTime(_createdAt.GetValue(entity)))
            SetTime(_createdAt, entity, audit.Now);

        if (_createdBy is not null && IsUnsetActor(_createdBy.GetValue(entity)))
            _createdBy.SetValue(entity, audit.CurrentActor);

        if (_updatedAt is not null)
            SetTime(_updatedAt, entity, audit.Now);

        if (_updatedBy is not null)
            _updatedBy.SetValue(entity, audit.CurrentActor);

        if (_updatedReason is not null && audit.Reason is not null)
            _updatedReason.SetValue(entity, audit.Reason);
    }

    private static void SetTime(PropertyInfo property, object entity, DateTimeOffset now) {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(entity, type == typeof(DateTime) ? now.UtcDateTime : now);
    }

    private static bool IsUnsetTime(object? value) => value switch {
        null => true,
        DateTimeOffset dto => dto == default,
        DateTime dt => dt == default,
        _ => false
    };

    private static bool IsUnsetActor(object? value) => value is null || (value is string s && s.Length == 0);
}
