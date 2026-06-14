using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aedis.Commands.Extensions;

/// <summary>
///     Extensions para configuração de Commands no DI.
/// </summary>
public static class CommandServiceCollectionExtensions
{
    /// <summary>
    ///     Registra a infraestrutura de Commands no DI.
    ///     Nota: Os handlers de publicação de eventos são registrados automaticamente
    ///     ao usar AddRabbitMqMessageBroker ou AddIbmMqMessageBroker.
    /// </summary>
    public static IServiceCollection AddCommands(this IServiceCollection services) {
        services.TryAddSingleton<ICommandExecutor, CommandExecutor>();
        return services;
    }

    /// <summary>
    ///     Registra um command handler customizado no DI.
    /// </summary>
    /// <typeparam name="TCommand">Tipo do comando.</typeparam>
    /// <typeparam name="TResult">Tipo do resultado.</typeparam>
    /// <typeparam name="THandler">Tipo do handler.</typeparam>
    public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult> {
        services.AddTransient<ICommandHandler<TCommand, TResult>, THandler>();
        return services;
    }
}