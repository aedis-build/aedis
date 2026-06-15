using Aedis.Commands;
using Aedis.Commands.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI da infraestrutura de Commands (CQRS) do Aedis.
/// </summary>
public static class CommandServiceCollectionExtensions
{
    /// <summary>Registra a infraestrutura de Commands (o <see cref="ICommandExecutor" />).</summary>
    public static IServiceCollection AddAedisCommands(this IServiceCollection services) {
        services.TryAddSingleton<ICommandExecutor, CommandExecutor>();
        return services;
    }

    /// <summary>Registra um command handler.</summary>
    public static IServiceCollection AddAedisCommandHandler<TCommand, TResult, THandler>(
        this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult> {
        services.AddTransient<ICommandHandler<TCommand, TResult>, THandler>();
        return services;
    }
}
