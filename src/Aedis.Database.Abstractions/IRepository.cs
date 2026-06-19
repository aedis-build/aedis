namespace Aedis.Database.Abstractions;

/// <summary>
///     Repositório completo de uma entidade: combina leitura (<see cref="IReadRepository{TEntity,TId}" />)
///     e escrita (<see cref="IWriteRepository{TEntity,TId}" />). Injete-o quando o componente precisa tanto
///     consultar quanto persistir; para dependências mais estritas, prefira a interface só de leitura ou só
///     de escrita.
/// </summary>
public interface IRepository<TEntity, TId> : IReadRepository<TEntity, TId>, IWriteRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull { }