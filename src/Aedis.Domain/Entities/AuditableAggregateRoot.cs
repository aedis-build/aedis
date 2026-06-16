namespace Aedis.Domain.Entities;

/// <summary>
///     Marca um agregado que <em>opta por auditoria</em>. Implementado por
///     <see cref="AuditableAggregateRoot{TId}" />; o provider de persistência carimba as colunas a partir
///     do contexto de auditoria (quem/quando/porquê) e — quando houver gerador de schema — força essas
///     colunas no <c>CREATE TABLE</c>.
/// </summary>
public interface IAuditable;

/// <summary>
///     Raiz de agregado <strong>auditável</strong> (opt-in): herde desta base no lugar de
///     <see cref="AggregateRoot{TId}" /> para ganhar, sem boilerplate, as colunas de auditoria e de
///     soft-delete. O usuário não gerencia esses campos — o repositório os preenche automaticamente a
///     partir do <c>IAuditContext</c> (CreatedAt/By na criação, UpdatedAt/By a cada escrita,
///     UpdatedReason quando informado, e DeletedAt/By no soft-delete).
/// </summary>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable
    where TId : notnull
{
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Motivo da última alteração (preenchido a partir de <c>IAuditContext.Reason</c>).</summary>
    public string? UpdatedReason { get; set; }

    // Soft-delete: o repositório reconhece IsDeleted e, ao deletar, marca quem/quando.
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
