namespace Aedis.Database.Abstractions;

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> CreateWriteSessionAsync(CancellationToken ct = default);

    Task<IUnitOfWork> CreateReadSessionAsync(CancellationToken ct = default);
}