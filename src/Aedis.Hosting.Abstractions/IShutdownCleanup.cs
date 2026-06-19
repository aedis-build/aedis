namespace Aedis.Hosting.Abstractions;

/// <summary>
///     Hook de limpeza executado durante o desligamento gracioso da aplicação.
/// </summary>
public interface IShutdownCleanup
{
    /// <summary>
    ///     Executa a limpeza ao desligar a aplicação. Implemente para liberar recursos próprios
    ///     (conexões, arquivos, flush de buffers); chamado pelo host durante o shutdown gracioso.
    /// </summary>
    /// <param name="cancellationToken">Token que sinaliza o limite de tempo do desligamento.</param>
    Task CleanupAsync(CancellationToken cancellationToken);
}
