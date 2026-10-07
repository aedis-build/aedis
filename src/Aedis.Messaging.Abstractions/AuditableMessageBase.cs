namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Base prática para mensagens auditáveis: herda correlação e data de <see cref="MessageBase" /> e expõe
///     os campos de <see cref="IAuditableMessage" /> como propriedades inicializáveis. A subclasse declara
///     <see cref="MessageBase.EventName" /> e preenche ação, sujeito, autor e mudanças.
/// </summary>
public abstract class AuditableMessageBase : MessageBase, IAuditableMessage
{
    /// <inheritdoc />
    public string Action { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Subject { get; init; } = string.Empty;

    /// <inheritdoc />
    public string? Author { get; init; }

    /// <inheritdoc />
    public IReadOnlyList<AuditFieldChange> Changes { get; init; } = [];
}
