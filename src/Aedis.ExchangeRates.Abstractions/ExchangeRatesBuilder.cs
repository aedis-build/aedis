using Microsoft.Extensions.DependencyInjection;

namespace Aedis.ExchangeRates.Abstractions;

/// <summary>
///     Ajustes fluentes após <c>AddAedisExchangeRates</c>. Pacotes de provider estendem este builder (ex.:
///     <c>WithPtax()</c>) registrando o <see cref="IExchangeRates" />.
/// </summary>
public sealed class ExchangeRatesBuilder(IServiceCollection services)
{
    /// <summary>Coleção de serviços em configuração.</summary>
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
