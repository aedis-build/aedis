using Aedis.Cache.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.Cache;

/// <summary>
///     Cria um <see cref="IExecutionCacheContext" /> por execução. Registrado como singleton; cada
///     <see cref="Create" /> devolve um contexto novo (com seu próprio estado de commit).
/// </summary>
public sealed class ExecutionCacheContextFactory(ICache cache, ILogger<ExecutionCacheContext> logger)
    : IExecutionCacheContextFactory
{
    public IExecutionCacheContext Create() => new ExecutionCacheContext(cache, logger);
}
