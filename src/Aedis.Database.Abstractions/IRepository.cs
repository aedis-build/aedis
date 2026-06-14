namespace Aedis.Database.Abstractions;

public interface IRepository<TEntity, TId> : IReadRepository<TEntity, TId>, IWriteRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull { }