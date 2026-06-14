using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aedis.Domain.ChainOfResponsibility;

/// <summary>
///     Extensões para configuração de Chain of Responsibility no DI container
/// </summary>
public static class ChainOfResponsibilityServiceCollectionExtensions
{
    /// <summary>
    ///     Registra um <see cref="IChainExecutor{TRequest, TResponse}" /> no container de DI usando um builder de chain.
    ///     Simplifica o registro de chains de responsabilidade, reduzindo boilerplate.
    /// </summary>
    /// <typeparam name="TRequest">O tipo da requisição</typeparam>
    /// <typeparam name="TResponse">O tipo da resposta</typeparam>
    /// <param name="services">A coleção de serviços</param>
    /// <param name="chainBuilder">Factory function que cria a chain de handlers</param>
    /// <param name="lifetime">O lifetime do serviço (padrão: Scoped)</param>
    /// <returns>A coleção de serviços para encadeamento</returns>
    /// <example>
    ///     <code>
    ///     // Singleton (handlers stateless)
    ///     services.AddChainExecutor&lt;FileWatcherContext, FileWatcherContext&gt;(
    ///         FileWatcherChainBuilder.BuildChain,
    ///         ServiceLifetime.Singleton);
    ///     
    ///     // Scoped (handlers com banco de dados)
    ///     services.AddChainExecutor&lt;DeliverNotificationRequest, DeliverNotificationRequest&gt;(
    ///         DeliveryChainBuilder.BuildDeliveryChain,
    ///         ServiceLifetime.Scoped);
    ///     </code>
    /// </example>
    public static IServiceCollection AddChainExecutor<TRequest, TResponse>(
        this IServiceCollection services,
        Func<IServiceProvider, IHandler<TRequest, TResponse>> chainBuilder,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequest : notnull {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(chainBuilder);

        services.Add(new ServiceDescriptor(
            typeof(IChainExecutor<TRequest, TResponse>),
            sp => {
                var logger = sp.GetRequiredService<ILogger<ChainExecutor<TRequest, TResponse>>>();
                var chain = chainBuilder(sp);

                if (chain == null)
                    throw new InvalidOperationException(
                        $"Chain builder returned null for {typeof(TRequest).Name} -> {typeof(TResponse).Name}");

                return new ChainExecutor<TRequest, TResponse>(chain, logger);
            },
            lifetime));

        return services;
    }
}