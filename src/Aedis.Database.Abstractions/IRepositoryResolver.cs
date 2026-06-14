namespace Aedis.Database.Abstractions;

/// <summary>
///     Resolve repositórios por tipo de entidade.
///     Utilizado para persistência em cascata de agregados.
/// </summary>
public interface IRepositoryResolver
{
    /// <summary>
    ///     Obtém o repositório para o tipo de entidade especificado.
    /// </summary>
    /// <param name="entityType">Tipo da entidade do repositório desejado.</param>
    /// <returns>Instância de repositório.</returns>
    object GetRepository(Type entityType);
}