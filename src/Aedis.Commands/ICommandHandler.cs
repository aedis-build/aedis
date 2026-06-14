namespace Aedis.Commands;

/// <summary>
///     Handler responsável por executar um comando específico.
///     Pode ser decorado com retry, circuit breaker, logging, etc.
/// </summary>
/// <typeparam name="TCommand">Tipo do comando a ser executado.</typeparam>
/// <typeparam name="TResult">Tipo do resultado da execução.</typeparam>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    /// <summary>
    ///     Executa o comando de forma assíncrona.
    /// </summary>
    /// <param name="command">Comando a ser executado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado da execução do comando.</returns>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}