namespace Aedis.Domain.Strategy.Abstractions;

public interface IStrategyResolver<TContext>
{
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}