namespace Aedis.ExchangeRates.Abstractions;

/// <summary>
///     Cotação de <paramref name="BaseCurrency" /> em <paramref name="QuoteCurrency" />: 1 unidade da moeda base vale
///     <paramref name="Buy" />/<paramref name="Sell" /> unidades da moeda cotada (ex.: USD/BRL 5,0203). Para converter um
///     preço que a empresa recebe em moeda estrangeira, use a taxa de venda.
/// </summary>
/// <param name="BaseCurrency">Moeda base, ISO 4217 (ex.: <c>USD</c>).</param>
/// <param name="QuoteCurrency">Moeda cotada, ISO 4217 (ex.: <c>BRL</c>).</param>
/// <param name="Buy">Taxa de compra.</param>
/// <param name="Sell">Taxa de venda.</param>
/// <param name="QuotedAt">Instante da publicação da cotação pela fonte.</param>
/// <param name="Source">Fonte (ex.: <c>PTAX</c>), para registro junto do valor convertido.</param>
public sealed record ExchangeRate(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Buy,
    decimal Sell,
    DateTimeOffset QuotedAt,
    string Source);
