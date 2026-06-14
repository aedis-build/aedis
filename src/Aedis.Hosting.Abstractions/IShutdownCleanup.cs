namespace Aedis.Hosting.Abstractions;

/// <summary>
///     Hook de limpeza executado durante o desligamento gracioso da aplicação.
/// </summary>
public interface IShutdownCleanup
{
    Task CleanupAsync(CancellationToken cancellationToken);
}
