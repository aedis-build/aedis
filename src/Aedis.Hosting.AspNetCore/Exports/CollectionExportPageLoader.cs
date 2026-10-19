using System.Runtime.CompilerServices;
using Aedis.Hosting.Abstractions;

namespace Aedis.Hosting.AspNetCore.Exports;

/// <summary>Busca de dados para exportação de coleções paginadas, por página ou em streaming de todas as páginas.</summary>
public static class CollectionExportPageLoader
{
    /// <summary>
    ///     Carrega os dados conforme o escopo: só a página pedida, ou todos os registros em blocos de
    ///     <paramref name="batchSize" /> materializados numa lista.
    /// </summary>
    public static async Task<IReadOnlyList<T>> LoadAsync<T>(CollectionExportMode mode, int page, int pageSize, int batchSize,
        Func<int, int, CancellationToken, Task<(IReadOnlyList<T> Items, int Total)>> fetchPageAsync,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(fetchPageAsync);

        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        if (mode == CollectionExportMode.Page) {
            var (items, _) = await fetchPageAsync(page, pageSize, cancellationToken);
            return items;
        }

        var result = new List<T>();
        await foreach (var item in StreamAsync(batchSize, fetchPageAsync, cancellationToken))
            result.Add(item);
        return result;
    }

    /// <summary>
    ///     Percorre todos os registros em streaming: busca as páginas sob demanda em blocos de
    ///     <paramref name="batchSize" /> e emite linha a linha, sem acumular o resultado em memória. Para quando
    ///     uma página vem vazia ou o total informado foi alcançado.
    /// </summary>
    public static async IAsyncEnumerable<T> StreamAsync<T>(int batchSize,
        Func<int, int, CancellationToken, Task<(IReadOnlyList<T> Items, int Total)>> fetchPageAsync,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(fetchPageAsync);
        batchSize = Math.Max(1, batchSize);

        var (firstItems, total) = await fetchPageAsync(1, batchSize, cancellationToken);
        foreach (var item in firstItems)
            yield return item;

        var emitted = firstItems.Count;
        if (emitted == 0 || emitted >= total)
            yield break;

        for (var currentPage = 2;; currentPage++) {
            var (items, _) = await fetchPageAsync(currentPage, batchSize, cancellationToken);
            if (items.Count == 0)
                yield break;

            foreach (var item in items)
                yield return item;

            emitted += items.Count;
            if (emitted >= total)
                yield break;
        }
    }
}
