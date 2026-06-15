using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aedis.Cache.Redis;

/// <summary>
///     Handle de um lock distribuído adquirido no Redis. Ao ser descartado, libera o lock — mas só se
///     <em>esta</em> instância ainda o detém (o <c>LockRelease</c> do Redis compara o valor antes de
///     apagar). O descarte é idempotente. Devolvido por
///     <see cref="RedisCache.IsLeaderAsync" /> e tipicamente registrado no <c>IDisposableRegistry</c>
///     para liberação automática no desligamento gracioso.
/// </summary>
internal sealed class RedisLock(IDatabase redisDb, string key, string value, ILogger logger) : IAsyncDisposable
{
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private bool _disposed;

    public async ValueTask DisposeAsync() {
        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseAsync(CancellationToken cancellationToken) {
        if (_disposed)
            return;

        try {
            var released = await redisDb.LockReleaseAsync(key, value).ConfigureAwait(false);

            if (released)
                logger.LogDebug("Lock liberado para a chave {Key} pela instância {Value} após {Elapsed}ms",
                    key, value, _timer.ElapsedMilliseconds);
            else
                logger.LogWarning("Falha ao liberar lock: a instância {Value} não detém mais o lock de {Key}",
                    value, key);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            logger.LogError(ex, "Erro ao liberar o lock no Redis para a chave {Key}", key);
        }
        finally {
            _disposed = true;
        }
    }
}
