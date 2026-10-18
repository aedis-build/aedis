using Aedis.ExchangeRates.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Ponto de entrada do registro do módulo de câmbio.</summary>
public static class ExchangeRatesServiceCollectionExtensions
{
    /// <summary>
    ///     Começa o registro do módulo de câmbio; escolha a fonte em seguida (ex.:
    ///     <c>services.AddAedisExchangeRates().WithPtax(configuration)</c>).
    /// </summary>
    public static ExchangeRatesBuilder AddAedisExchangeRates(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);
        return new ExchangeRatesBuilder(services);
    }
}
