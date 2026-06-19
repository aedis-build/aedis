namespace Aedis.Excel.Abstractions;

/// <summary>
///     Define uma coluna de uma planilha: o cabeçalho exibido e o seletor que extrai o valor de cada linha.
///     Use uma lista de colunas para descrever a saída tabular de forma declarativa ao chamar <see cref="IExcelWriter" />.
/// </summary>
/// <typeparam name="T">Tipo de cada linha de dados.</typeparam>
/// <param name="Header">Texto do cabeçalho da coluna.</param>
/// <param name="ValueSelector">Função que extrai o valor da célula a partir de uma linha; pode retornar nulo.</param>
public sealed record ExcelColumn<T>(string Header, Func<T, object?> ValueSelector);
