namespace Aedis.Commands.Abstractions;

/// <summary>
///     Executor de comandos responsável por encontrar o handler apropriado e executar o comando.
///     Suporta decorators para adicionar comportamentos transversais (retry, logging, etc).
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    ///     Executa um comando de forma assíncrona.
    /// </summary>
    /// <typeparam name="TResult">Tipo do resultado da execução.</typeparam>
    /// <param name="command">Comando a ser executado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado da execução do comando.</returns>
    Task<TResult> ExecuteAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}