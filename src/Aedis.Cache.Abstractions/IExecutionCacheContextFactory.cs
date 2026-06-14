namespace Aedis.Cache.Abstractions;

public interface IExecutionCacheContextFactory
{
    IExecutionCacheContext Create();
}