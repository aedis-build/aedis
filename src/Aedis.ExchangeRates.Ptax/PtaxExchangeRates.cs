using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aedis.ExchangeRates.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.ExchangeRates.Ptax;

/// <summary>
///     <see cref="IExchangeRates" /> sobre a PTAX do Banco Central do Brasil. Uma chamada a <c>CotacaoMoedaPeriodo</c>
///     traz a janela de <see cref="PtaxOptions.LookbackDays" /> dias até a data pedida, e vale o último boletim de
///     <c>Fechamento</c> (a PTAX oficial, publicada por volta das 13h de Brasília). Cota qualquer moeda da PTAX contra
///     o real; pares sem BRL como moeda cotada não são suportados.
/// </summary>
internal sealed class PtaxExchangeRates(HttpClient http, IOptions<PtaxOptions> options) : IExchangeRates
{
    /// <summary>Nome da fonte gravado em <see cref="ExchangeRate.Source" />.</summary>
    public const string SourceName = "PTAX";

    private const string ClosingBulletin = "Fechamento";
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    /// <inheritdoc />
    public async Task<ExchangeRate> GetAsync(string baseCurrency, string quoteCurrency, DateOnly date,
        CancellationToken cancellationToken = default) {
        var from = NormalizeCurrency(baseCurrency);
        var to = NormalizeCurrency(quoteCurrency);
        if (to != "BRL" || from == "BRL") throw new NotSupportedException($"A PTAX cota moeda estrangeira contra BRL; {from}/{to} não é suportado.");

        var start = date.AddDays(-options.Value.LookbackDays);
        var url = "CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)"
                  + $"?@moeda='{from}'&@dataInicial='{Format(start)}'&@dataFinalCotacao='{Format(date)}'"
                  + "&$format=json&$select=cotacaoCompra,cotacaoVenda,dataHoraCotacao,tipoBoletim";

        var response = await http.GetFromJsonAsync<PtaxResponse>(url, cancellationToken);
        var closing = (response?.Value ?? [])
            .Where(q => q.Bulletin == ClosingBulletin)
            .Select(q => (Quote: q, At: ParseBrasilia(q.QuotedAt)))
            .Where(q => DateOnly.FromDateTime(q.At.DateTime) <= date)
            .OrderByDescending(q => q.At)
            .FirstOrDefault();

        if (closing.Quote is null)
            throw new ExchangeRateUnavailableException($"PTAX sem fechamento de {from}/BRL entre {start:yyyy-MM-dd} e {date:yyyy-MM-dd}.");

        return new ExchangeRate(from, to, closing.Quote.Buy, closing.Quote.Sell, closing.At, SourceName);
    }

    private static string NormalizeCurrency(string currency) {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var code = currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"Moeda '{currency}' não é um código ISO 4217 de três letras.", nameof(currency));
        return code;
    }

    private static string Format(DateOnly date) => date.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);

    /// <summary>A PTAX publica no horário de Brasília (UTC−3, sem horário de verão desde 2019).</summary>
    private static DateTimeOffset ParseBrasilia(string value) =>
        new(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None), BrasiliaOffset);

    private sealed record PtaxResponse([property: JsonPropertyName("value")] List<PtaxQuote> Value);

    private sealed record PtaxQuote(
        [property: JsonPropertyName("cotacaoCompra")] decimal Buy,
        [property: JsonPropertyName("cotacaoVenda")] decimal Sell,
        [property: JsonPropertyName("dataHoraCotacao")] string QuotedAt,
        [property: JsonPropertyName("tipoBoletim")] string Bulletin);
}
