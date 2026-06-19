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
    /// <summary>Instante de criação do registro (preenchido pelo repositório na inserção).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Autor da criação (preenchido a partir de <c>IAuditContext</c> na inserção).</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Instante da última escrita (preenchido pelo repositório a cada atualização).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Autor da última escrita (preenchido a partir de <c>IAuditContext</c> a cada atualização).</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>Motivo da última alteração (preenchido a partir de <c>IAuditContext.Reason</c>).</summary>
    public string? UpdatedReason { get; set; }

    /// <summary>
    ///     Marca de soft-delete: o repositório reconhece esta flag e, ao deletar, marca o registro como
    ///     excluído em vez de removê-lo fisicamente, carimbando <see cref="DeletedAt" /> e
    ///     <see cref="DeletedBy" />.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>Instante do soft-delete (preenchido pelo repositório ao excluir).</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Autor do soft-delete (preenchido a partir de <c>IAuditContext</c> ao excluir).</summary>
    public string? DeletedBy { get; set; }
}
