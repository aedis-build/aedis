using Microsoft.Extensions.Logging;
using Aedis.Commands.Abstractions;

namespace Aedis.Commands.Decorators;

/// <summary>
///     Decorator que adiciona retry automático à execução de comandos.
///     Usa exponential backoff para aguardar entre tentativas.
/// </summary>
/// <typeparam name="TCommand">Tipo do comando.</typeparam>
/// <typeparam name="TResult">Tipo do resultado.</typeparam>
public class RetryCommandHandlerDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly TimeSpan _initialDelay;
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly ILogger<RetryCommandHandlerDecorator<TCommand, TResult>> _logger;
    private readonly int _maxRetries;

    /// <summary>
    ///     Decora o handler interno configurando o número máximo de tentativas e o atraso inicial do backoff
    ///     exponencial (padrão: 3 tentativas, 100 ms).
    /// </summary>
    public RetryCommandHandlerDecorator(
        ICommandHandler<TCommand, TResult> inner,
        ILogger<RetryCommandHandlerDecorator<TCommand, TResult>> logger,
        int maxRetries = 3,
        TimeSpan? initialDelay = null) {
        _inner = inner;
        _logger = logger;
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
    }

    /// <summary>
    ///     Executa o handler interno, repetindo em caso de exceção com atraso em backoff exponencial até
    ///     atingir o limite de tentativas; após a última falha, loga e re-lança a exceção.
    /// </summary>
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        var commandName = typeof(TCommand).Name;
        var attempt = 0;

        while (true) {
            attempt++;

            try {
                return await _inner.HandleAsync(command, cancellationToken);
            }
            catch (Exception ex) when (attempt < _maxRetries) {
                var delay = TimeSpan.FromMilliseconds(_initialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

                _logger.LogWarning(ex,
                    "Command {CommandName} failed on attempt {Attempt}/{MaxRetries}. Retrying in {DelayMs}ms...",
                    commandName, attempt, _maxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) {
                _logger.LogError(ex,
                    "Command {CommandName} failed after {MaxRetries} attempts",
                    commandName, _maxRetries);

                throw;
            }
        }
    }
}