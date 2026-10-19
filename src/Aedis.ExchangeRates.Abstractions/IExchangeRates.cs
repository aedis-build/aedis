namespace Aedis.ExchangeRates.Abstractions;

/// <summary>
///     Porta de cotação de câmbio. A fonte é escolhida na composition root (ex.: PTAX do Banco Central em
///     <c>Aedis.ExchangeRates.Ptax</c>); quem converte valores depende só deste contrato.
/// </summary>
public interface IExchangeRates
{
    /// <summary>
    ///     Cotação oficial de fechamento mais recente publicada <b>até</b> <paramref name="date" /> (em fim de semana,
    ///     feriado ou antes da publicação do dia, vale a do último dia útil).
    /// </summary>
    /// <param name="baseCurrency">Moeda base (ex.: <c>USD</c>).</param>
    /// <param name="quoteCurrency">Moeda cotada (ex.: <c>BRL</c>).</param>
    /// <param name="date">Data de referência.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <exception cref="NotSupportedException">A fonte não cota esse par.</exception>
    /// <exception cref="ExchangeRateUnavailableException">Sem cotação na janela consultada.</exception>
    Task<ExchangeRate> GetAsync(string baseCurrency, string quoteCurrency, DateOnly date, CancellationToken cancellationToken = default);
}
