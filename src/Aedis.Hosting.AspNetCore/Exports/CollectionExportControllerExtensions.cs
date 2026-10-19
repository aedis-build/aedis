using Aedis.Excel.Abstractions;
using Aedis.Exceptions;
using Aedis.Hosting.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.Hosting.AspNetCore.Exports;

/// <summary>
///     Exportação de coleções a partir do mesmo endpoint que lista: quando o request traz um
///     <see cref="ExportRequest" /> (via <see cref="IExportable" />), a action produz o arquivo em vez da
///     listagem. No escopo <see cref="CollectionExportMode.All" /> as páginas são lidas em streaming e escritas
///     pelo <see cref="IExcelWriter" /> sem materializar o resultado inteiro.
/// </summary>
/// <example>
///     <code>
///     [HttpQuery]
///     public async Task&lt;IActionResult&gt; Search([FromBody] SearchOrdersRequest request, CancellationToken ct) {
///         var export = await this.TryHandleCollectionExportAsync(_excel, request.Export, request.Page ?? 1, request.PageSize ?? 50,
///             (p, s, c) =&gt; LoadPageAsync(request, p, s, c), Columns, "orders", cancellationToken: ct);
///         if (export is not null) return export;
///
///         return OkCollection(await LoadAsync(request, ct), ExportLinkOptions.XlsxOnly);
///     }
///     </code>
/// </example>
public static class CollectionExportControllerExtensions
{
    /// <summary>
    ///     Processa a exportação quando <paramref name="export" /> está preenchido e devolve o resultado já
    ///     escrito na resposta; <c>null</c> quando não há exportação (a action segue com a listagem). Formato não
    ///     suportado ou não permitido lança <see cref="BusinessException" /> com
    ///     <see cref="ViolationType.PreconditionFailed" /> (412).
    /// </summary>
    /// <typeparam name="T">Tipo de cada linha exportada.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="excelWriter">Motor de escrita (XLSX/CSV).</param>
    /// <param name="export">Descritor vindo do request; <c>null</c> = sem exportação.</param>
    /// <param name="page">Página corrente (escopo <see cref="CollectionExportMode.Page" />).</param>
    /// <param name="pageSize">Tamanho da página corrente.</param>
    /// <param name="fetchPageAsync">Busca uma página (número, tamanho) devolvendo os itens e o total.</param>
    /// <param name="columns">Colunas do arquivo, na ordem.</param>
    /// <param name="fileName">Nome do arquivo sem extensão.</param>
    /// <param name="exportBatchSize">Tamanho dos blocos lidos no escopo <c>All</c>. Padrão 200.</param>
    /// <param name="allowedFormats">Formatos aceitos por este endpoint. Padrão: só XLSX.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task<IActionResult?> TryHandleCollectionExportAsync<T>(this ControllerBase controller,
        IExcelWriter excelWriter, ExportRequest? export, int page, int pageSize,
        Func<int, int, CancellationToken, Task<(IReadOnlyList<T> Items, int Total)>> fetchPageAsync,
        IReadOnlyList<ExcelColumn<T>> columns, string fileName, int exportBatchSize = 200,
        IReadOnlyCollection<ExportFormat>? allowedFormats = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(excelWriter);
        ArgumentNullException.ThrowIfNull(fetchPageAsync);
        ArgumentNullException.ThrowIfNull(columns);

        if (export is null)
            return null;

        var allowed = allowedFormats ?? [ExportFormat.Xlsx];
        if (!allowed.Contains(export.Type)) {
            var names = string.Join(", ", allowed.Select(f => f.ToString().ToLowerInvariant()));
            throw new BusinessException($"Formato de exportação não permitido neste endpoint. Valores aceitos: {names}.",
                ViolationType.PreconditionFailed, "export-format-not-allowed", StatusCodes.Status412PreconditionFailed);
        }

        var format = export.Type == ExportFormat.Csv ? ExcelFormat.Csv : ExcelFormat.Xlsx;
        ExcelResult result;

        if (export.Mode == CollectionExportMode.All) {
            var rows = CollectionExportPageLoader.StreamAsync(exportBatchSize, fetchPageAsync, cancellationToken)
                .ToBlockingEnumerable(cancellationToken);
            result = await excelWriter.WriteAsync(rows, columns, fileName, format, estimatedRowCount: int.MaxValue,
                cancellationToken: cancellationToken);
        }
        else {
            var data = await CollectionExportPageLoader.LoadAsync(export.Mode, page, pageSize, exportBatchSize, fetchPageAsync, cancellationToken);
            result = await excelWriter.WriteAsync(data, columns, fileName, format, estimatedRowCount: data.Count,
                cancellationToken: cancellationToken);
        }

        await using (result) {
            controller.Response.ContentType = result.ContentType;
            controller.Response.Headers.ContentDisposition = result.ContentDisposition;
            await result.Stream.CopyToAsync(controller.Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }
}
