using Aedis.App1.Domain.Entities;
using Aedis.Database.Postgres.Queries;

namespace Aedis.App1.Infrastructure.Queries;

/// <summary>
///     Critério que localiza uma notificação ativa pela chave de negócio <see cref="Notification.Code" />. As
///     colunas são referenciadas em <c>snake_case</c>, em paridade com a convenção do provider PostgreSQL.
/// </summary>
public sealed class NotificationByCodeCriteria : PostgresCriteria<Notification> {
    /// <summary>
    ///     Cria o critério para um código específico.
    /// </summary>
    /// <param name="code">Chave de negócio a localizar.</param>
    public NotificationByCodeCriteria(string code) : base("notification") {
        WhereEquals("is_deleted", false)
            .And()
            .WhereEquals("code", code)
            .Limit(1);
    }
}
