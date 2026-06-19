namespace Aedis.Excel.Abstractions;

/// <summary>
///     Contrato agnóstico de motor para gerar uma planilha (XLSX) ou CSV a partir de linhas e colunas.
///     O código de aplicação depende apenas desta interface — não da biblioteca concreta que produz o arquivo.
///     Cada coluna declara um cabeçalho e um seletor que extrai o valor de cada linha.
/// </summary>
public interface IExcelWriter
{
    /// <summary>
    ///     Escreve <paramref name="rows" /> nas <paramref name="columns" /> informadas e devolve o resultado
    ///     como stream pronto para download. Use ao exportar dados tabulares para o usuário; a implementação
    ///     decide entre buffer em memória ou arquivo temporário conforme o volume estimado.
    /// </summary>
    /// <typeparam name="T">Tipo de cada linha de dados.</typeparam>
    /// <param name="rows">Sequência de linhas a serializar; pode ser enumerada de forma preguiçosa.</param>
    /// <param name="columns">Colunas na ordem de saída, cada uma com cabeçalho e seletor de valor.</param>
    /// <param name="fileName">Nome de arquivo desejado (sem extensão); a implementação o sanitiza.</param>
    /// <param name="format">Formato de saída: <see cref="ExcelFormat.Xlsx" /> (padrão) ou <see cref="ExcelFormat.Csv" />.</param>
    /// <param name="delimiter">Delimitador usado apenas quando o formato é CSV.</param>
    /// <param name="estimatedRowCount">Estimativa de linhas para a implementação escolher memória ou arquivo temporário; 0 quando desconhecido.</param>
    /// <param name="cancellationToken">Token para cancelar a operação.</param>
    /// <returns>Resultado com o stream do arquivo e os metadados de resposta HTTP.</returns>
    Task<ExcelResult> WriteAsync<T>(
        IEnumerable<T> rows,
        IReadOnlyList<ExcelColumn<T>> columns,
        string fileName,
        ExcelFormat format = ExcelFormat.Xlsx,
        CsvDelimiter delimiter = CsvDelimiter.Comma,
        int estimatedRowCount = 0,
        CancellationToken cancellationToken = default);
}
