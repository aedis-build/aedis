namespace Aedis.Security.Abstractions;

/// <summary>
///     Fornece o "quem" e o "quando" para campos de auditoria (CreatedBy/UpdatedBy, CreatedAt/UpdatedAt),
///     eliminando o <em>threading</em> manual desses valores pela aplicação.
/// </summary>
public interface IAuditContext
{
    /// <summary>Identificador do ator atual para CreatedBy/UpdatedBy (ou null para sistema/anônimo).</summary>
    string? CurrentActor { get; }

    /// <summary>Instante atual (UTC) para CreatedAt/UpdatedAt.</summary>
    DateTimeOffset Now { get; }
}
