namespace Aedis.Cache.Abstractions;

/// <summary>
///     Serviço de processamento idempotente de lotes sobre um <see cref="ICache" />: elege o líder do lote,
///     persiste/recupera o checkpoint de linha para retomada, deduplica itens já processados e contabiliza
///     o progresso. Habilitado por <c>AddAedisBatchCache()</c>.
/// </summary>
public interface IBatchCache
{
    /// <summary>
    ///     Tenta assumir a liderança do lote <paramref name="batchId" /> (lock expira em
    ///     <paramref name="expiration" />) e, se conseguir, devolve o checkpoint para retomar do ponto salvo.
    ///     Descartar o checkpoint libera a liderança.
    /// </summary>
    /// <returns>O checkpoint (linha salva + handle de liderança), ou <c>null</c> se outra instância já lidera o lote.</returns>
    Task<IBatchCheckpoint?> GetCheckpointAsync(string batchId, TimeSpan expiration, CancellationToken ct = default);

    /// <summary>Persiste a <paramref name="line" /> já processada do lote, para permitir retomada posterior.</summary>
    Task UpdateCheckpointAsync(string batchId, int line, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marca o item <paramref name="uuid" /> do lote como processado, de forma atômica e idempotente.
    /// </summary>
    /// <returns><c>true</c> se a marcação é nova; <c>false</c> se o item já estava processado (deduplicado).</returns>
    Task<bool> MarkProcessedAsync(string batchId, string uuid, CancellationToken cancellationToken = default);

    /// <summary>Incrementa o contador de progresso do lote <paramref name="batchId" />.</summary>
    Task IncrementProgressAsync(string batchId, CancellationToken ct = default);
}