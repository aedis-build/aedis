namespace Aedis.Security.Abstractions;

/// <summary>
///     Fornece o "quem", o "quando" e o "porquê" para campos de auditoria (CreatedBy/UpdatedBy,
///     CreatedAt/UpdatedAt, UpdatedReason), eliminando o <em>threading</em> manual desses valores pela
///     aplicação.
/// </summary>
public interface IAuditContext
{
    /// <summary>Identificador do ator atual para CreatedBy/UpdatedBy (ou null para sistema/anônimo).</summary>
    string? CurrentActor { get; }

    /// <summary>Instante atual (UTC) para CreatedAt/UpdatedAt.</summary>
    DateTimeOffset Now { get; }

    /// <summary>
    ///     Motivo da operação atual, gravado em <c>UpdatedReason</c> quando a entidade tem essa coluna.
    ///     Default <c>null</c> — implementações que rastreiam o "porquê" sobrescrevem.
    /// </summary>
    string? Reason => null;
}
