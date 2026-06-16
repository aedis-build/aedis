namespace Aedis.Domain.Entities;

/// <summary>Marca um Aggregate Root — a raiz de consistência de um agregado de domínio.</summary>
public interface IAggregateRoot;

/// <summary>
///     Base de entidade de domínio: identidade (<see cref="Id" />) e igualdade por identidade + tipo
///     concreto. Entidades transientes (Id default, ainda não persistidas) usam igualdade por referência.
/// </summary>
public abstract class EntityBase<TId> : IEquatable<EntityBase<TId>>
    where TId : notnull
{
    public TId Id { get; set; } = default!;

    private bool IsTransient => EqualityComparer<TId>.Default.Equals(Id, default!);

    public bool Equals(EntityBase<TId>? other) {
        if (other is null || other.GetType() != GetType()) return false;
        if (ReferenceEquals(this, other)) return true;
        if (IsTransient || other.IsTransient) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as EntityBase<TId>);

    public override int GetHashCode() => IsTransient ? base.GetHashCode() : Id.GetHashCode();
}

/// <summary>
///     Raiz de agregado de negócio: uma <see cref="EntityBase{TId}" /> que delimita uma fronteira de
///     consistência. Carrega apenas a entidade de negócio — auditoria é opcional (opt-in) via
///     <see cref="AuditableAggregateRoot{TId}" />.
/// </summary>
public abstract class AggregateRoot<TId> : EntityBase<TId>, IAggregateRoot
    where TId : notnull;
