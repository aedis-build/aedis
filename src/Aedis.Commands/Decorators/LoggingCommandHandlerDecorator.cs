using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Aedis.Commands.Decorators;

/// <summary>
///     Decorator que adiciona logging detalhado à execução de comandos.
///     Registra início, fim, duração e erros da execução.
/// </summary>
/// <typeparam name="TCommand">Tipo do comando.</typeparam>
/// <typeparam name="TResult">Tipo do resultado.</typeparam>
public class LoggingCommandHandlerDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly ILogger<LoggingCommandHandlerDecorator<TCommand, TResult>> _logger;

    public LoggingCommandHandlerDecorator(
        ICommandHandler<TCommand, TResult> inner,
        ILogger<LoggingCommandHandlerDecorator<TCommand, TResult>> logger) {
        _inner = inner;
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        var commandName = typeof(TCommand).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Executing command {CommandName}", commandName);

        try {
            var result = await _inner.HandleAsync(command, cancellationToken);

            stopwatch.Stop();

            _logger.LogDebug(
                "Command {CommandName} executed successfully in {ElapsedMs}ms",
                commandName, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex) {
            stopwatch.Stop();

            _logger.LogError(ex,
                "Command {CommandName} failed after {ElapsedMs}ms",
                commandName, stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}