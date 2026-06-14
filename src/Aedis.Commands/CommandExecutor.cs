using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aedis.Commands;

/// <summary>
///     Implementação padrão do executor de comandos.
///     Resolve handlers via DI e delega a execução.
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    private readonly ILogger<CommandExecutor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public CommandExecutor(IServiceScopeFactory scopeFactory, ILogger<CommandExecutor> logger) {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default) {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var commandType = command.GetType();
        var resultType = typeof(TResult);
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);

        _logger.LogDebug("Resolving handler for command {CommandType}", commandType.Name);

        var handler = scope.ServiceProvider.GetService(handlerType);

        if (handler == null)
            throw new InvalidOperationException(
                $"No handler registered for command {commandType.Name}. " +
                $"Please register ICommandHandler<{commandType.Name}, {resultType.Name}> in DI.");

        _logger.LogTrace("Executing command {CommandType}", commandType.Name);

        var handleMethod = handlerType.GetMethod(nameof(ICommandHandler<ICommand<TResult>, TResult>.HandleAsync));

        if (handleMethod == null)
            throw new InvalidOperationException($"HandleAsync method not found on handler {handlerType.Name}");

        var task = (Task<TResult>)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
        var result = await task;

        _logger.LogTrace("Command {CommandType} executed successfully", commandType.Name);

        return result;
    }
}