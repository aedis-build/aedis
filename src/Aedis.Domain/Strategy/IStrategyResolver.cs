namespace Aedis.Domain.Strategy;

public interface IStrategyResolver<TContext>
{
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}