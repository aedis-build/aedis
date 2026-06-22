using Aedis.App1.Application.Abstractions;
using Aedis.App1.Domain.Entities;
using Aedis.App1.Infrastructure.Queries;
using Aedis.Database.Abstractions;

namespace Aedis.App1.Infrastructure.Repositories;

/// <summary>
///     Implementação PostgreSQL de <see cref="INotificationRepository" />. Compõe o repositório genérico do
///     Aedis (<see cref="IRepository{TEntity,TId}" />, registrado por <c>AddAedisPostgres</c>) para o upsert e
///     acrescenta a consulta por chave de negócio via <see cref="NotificationByCodeCriteria" />.
/// </summary>
public sealed class NotificationRepository : INotificationRepository {
    private readonly IRepository<Notification, Guid> _repository;

    /// <summary>
    ///     Cria o repositório compondo o repositório genérico do Aedis.
    /// </summary>
    /// <param name="repository">Repositório genérico do agregado, provido pelo provider PostgreSQL.</param>
    public NotificationRepository(IRepository<Notification, Guid> repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Notification?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) {
        var found = await _repository.FindAsync(new NotificationByCodeCriteria(code), cancellationToken);
        return found.FirstOrDefault();
    }

    /// <inheritdoc />
    public Task<Notification> SaveAsync(Notification notification, CancellationToken cancellationToken = default) {
        return _repository.SaveAsync(notification, cancellationToken);
    }
}
