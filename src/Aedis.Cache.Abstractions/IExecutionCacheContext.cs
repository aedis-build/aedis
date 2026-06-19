namespace Aedis.Cache.Abstractions;

/// <summary>
///     Contexto de uma execução (job/ciclo) sobre um <see cref="ICache" />: expõe o instante da última
///     execução, deduplica itens já processados e confirma (commit) o avanço do marcador. Descartar sem
///     commit sinaliza que a janela não avançou. Obtido via <see cref="IExecutionCacheContextFactory" />.
/// </summary>
public interface IExecutionCacheContext : IAsyncDisposable
{
    /// <summary>Instante da última execução confirmada, ou <c>null</c> se ainda não houve nenhuma.</summary>
    Task<DateTimeOffset?> GetLastExecution(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marca <paramref name="value" /> como processado nesta janela, de forma atômica e idempotente.
    /// </summary>
    /// <returns><c>true</c> se a marcação é nova; <c>false</c> se já estava processado (deduplicado).</returns>
    Task<bool> MarkAsProcessedAsync(string value, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Confirma a execução, gravando o instante atual como marcador de última execução. Chame ao fim de
    ///     um ciclo bem-sucedido; sem isto, o descarte avisa que a janela não avançou.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}