using Aedis.App1.Domain.Entities;

namespace Aedis.App1.Application.Abstractions;

/// <summary>
///     Porta de saída do agregado <see cref="Notification" />. A aplicação depende apenas deste contrato; a
///     implementação concreta (PostgreSQL) vive na Infrastructure.
/// </summary>
public interface INotificationRepository {
    /// <summary>
    ///     Recupera uma notificação pela chave de negócio <see cref="Notification.Code" />, ou <c>null</c>
    ///     quando não existe.
    /// </summary>
    Task<Notification?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Persiste a notificação (upsert) e devolve a entidade com as colunas de auditoria carimbadas.
    /// </summary>
    Task<Notification> SaveAsync(Notification notification, CancellationToken cancellationToken = default);
}
