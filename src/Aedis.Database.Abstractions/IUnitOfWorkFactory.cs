namespace Aedis.Database.Abstractions;

/// <summary>
///     Cria sessões de unidade de trabalho (<see cref="IUnitOfWork" />). Separa o endpoint de escrita
///     (primário, transacional) do de leitura (réplicas), permitindo direcionar consultas para réplicas e
///     escritas para o primário.
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>Cria uma sessão de escrita transacional sobre o endpoint primário.</summary>
    Task<IUnitOfWork> CreateWriteSessionAsync(CancellationToken ct = default);

    /// <summary>Cria uma sessão somente leitura, tipicamente sobre uma réplica.</summary>
    Task<IUnitOfWork> CreateReadSessionAsync(CancellationToken ct = default);
}