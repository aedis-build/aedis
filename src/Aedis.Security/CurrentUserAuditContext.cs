using Aedis.Security.Abstractions;

namespace Aedis.Security;

/// <summary>
///     Ponte entre o usuário autenticado (<see cref="ICurrentUser" />) e o contexto de auditoria
///     (<see cref="IAuditContext" />): o <see cref="CurrentActor" /> passa a ser o usuário logado (Id, ou
///     Name como fallback). Quando não há usuário autenticado, devolve <c>null</c> — e o provider de
///     persistência grava o ator default configurado (ex.: <c>"system"</c>), deixando claro que a ação não
///     foi atribuída a um usuário logado. Registrar como <em>scoped</em> (por requisição).
/// </summary>
public sealed class CurrentUserAuditContext(ICurrentUser? currentUser = null) : IAuditContext
{
    /// <summary>Usuário logado (Id, ou Name como fallback) ou <c>null</c> quando anônimo/não autenticado.</summary>
    public string? CurrentActor => currentUser is { IsAuthenticated: true }
        ? currentUser.Id ?? currentUser.Name
        : null;

    /// <summary>Instante atual em UTC, usado para carimbar CreatedAt/UpdatedAt.</summary>
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>
    ///     Motivo da operação atual (gravado em <c>UpdatedReason</c>). Defina por operação dentro do escopo
    ///     antes de salvar; <c>null</c> por padrão.
    /// </summary>
    public string? Reason { get; set; }
}
