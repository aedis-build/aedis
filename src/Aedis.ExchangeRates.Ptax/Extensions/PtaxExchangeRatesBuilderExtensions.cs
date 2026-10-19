using Aedis.ExchangeRates.Abstractions;
using Aedis.ExchangeRates.Ptax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro do provider PTAX no módulo de câmbio.</summary>
public static class PtaxExchangeRatesBuilderExtensions
{
    /// <summary>
    ///     Usa a PTAX do Banco Central como <see cref="IExchangeRates" />: vincula <c>ExchangeRates:Ptax</c>
    ///     (<see cref="PtaxOptions" />, validado na subida; a URL tem de ser HTTPS) e registra um cliente HTTP tipado.
    ///     A API é pública e sem credencial.
    /// </summary>
    public static ExchangeRatesBuilder WithPtax(this ExchangeRatesBuilder builder, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var services = builder.Services;
        services.AddOptions<PtaxOptions>()
            .Bind(configuration.GetSection(PtaxOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"{PtaxOptions.SectionName}:BaseUrl deve ser uma URL HTTPS absoluta.")
            .Validate(o => o.Timeout > TimeSpan.Zero, $"{PtaxOptions.SectionName}:Timeout deve ser positivo.")
            .ValidateOnStart();

        services.AddHttpClient<IExchangeRates, PtaxExchangeRates>((sp, http) => {
            var options = sp.GetRequiredService<IOptions<PtaxOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
            http.Timeout = options.Timeout;
        });
        return builder;
    }
}
