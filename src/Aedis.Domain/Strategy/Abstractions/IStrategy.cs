namespace Aedis.Domain.Strategy.Abstractions;

public interface IStrategy<in TContext>
{
    bool CanHandle(TContext context);
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}