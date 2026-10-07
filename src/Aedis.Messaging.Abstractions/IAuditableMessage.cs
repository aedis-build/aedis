namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Mensagem que carrega uma trilha de auditoria: a ação executada, o sujeito afetado, o autor e as
///     mudanças campo a campo. Consumidores de auditoria (publicação de eventos de negócio, trilha
///     regulatória) dependem só deste contrato, sem conhecer a mensagem concreta.
/// </summary>
public interface IAuditableMessage : IMessage
{
    /// <summary>Ação executada sobre o sujeito, em forma curta e estável (ex.: <c>created</c>, <c>updated</c>, <c>cancelled</c>).</summary>
    string Action { get; }

    /// <summary>Identificação do sujeito afetado, sem dados pessoais (ex.: <c>order:42</c>).</summary>
    string Subject { get; }

    /// <summary>Quem executou a ação (identificador técnico do usuário ou do sistema), quando conhecido.</summary>
    string? Author { get; }

    /// <summary>Mudanças campo a campo produzidas pela ação; vazio quando a ação não altera estado observável.</summary>
    IReadOnlyList<AuditFieldChange> Changes { get; }
}
