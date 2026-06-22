using Aedis.Domain.Entities;

namespace Aedis.App1.Domain.Entities;

/// <summary>
///     Agregado de exemplo processado pelo worker. Herda de <see cref="AuditableAggregateRoot{TId}" />, então
///     ganha identidade, colunas de auditoria e soft-delete carimbados pelo repositório. Modela uma notificação
///     que nasce <c>Pending</c> e é marcada <c>Sent</c> ao ser processada — troque pelo seu próprio agregado.
/// </summary>
public class Notification : AuditableAggregateRoot<Guid> {
    /// <summary>
    ///     Chave de negócio (idempotência): identifica a notificação de forma estável entre reentregas.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    ///     Destinatário da notificação.
    /// </summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    ///     Conteúdo da notificação.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    ///     Estado atual. Persistido como texto maiúsculo pelo provider PostgreSQL.
    /// </summary>
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    /// <summary>
    ///     Cria uma notificação pendente com identidade gerada.
    /// </summary>
    /// <param name="code">Chave de negócio (idempotência).</param>
    /// <param name="recipient">Destinatário.</param>
    /// <param name="content">Conteúdo.</param>
    public static Notification Request(string code, string recipient, string content) {
        return new Notification {
            Id = Guid.NewGuid(),
            Code = code,
            Recipient = recipient,
            Content = content,
            Status = NotificationStatus.Pending
        };
    }

    /// <summary>
    ///     Marca a notificação como enviada. A transição de estado vive no domínio.
    /// </summary>
    public void MarkSent() {
        Status = NotificationStatus.Sent;
    }
}

/// <summary>
///     Estados possíveis de uma <see cref="Notification" />.
/// </summary>
public enum NotificationStatus {
    /// <summary>Criada, ainda não processada.</summary>
    Pending,

    /// <summary>Processada/enviada.</summary>
    Sent
}
