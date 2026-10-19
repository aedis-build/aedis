using System.Net;
using System.Text;
using Aedis.ExchangeRates.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.ExchangeRates.Ptax.Tests;

/// <summary>
///     PTAX: uma consulta à janela de dias até a data pedida, vale o último boletim de fechamento publicado até a data
///     (fim de semana cai na sexta), horário de Brasília, só moeda estrangeira contra BRL.
/// </summary>
public class PtaxExchangeRatesTests
{
    private const string Week = """
        {"value":[
          {"cotacaoCompra":5.01970,"cotacaoVenda":5.02030,"dataHoraCotacao":"2026-10-15 10:03:11.648598","tipoBoletim":"Abertura"},
          {"cotacaoCompra":5.01130,"cotacaoVenda":5.01190,"dataHoraCotacao":"2026-10-15 13:08:16.814037","tipoBoletim":"Fechamento"},
          {"cotacaoCompra":4.97700,"cotacaoVenda":4.97760,"dataHoraCotacao":"2026-10-16 13:07:30.550247","tipoBoletim":"Intermediário"},
          {"cotacaoCompra":4.98860,"cotacaoVenda":4.98920,"dataHoraCotacao":"2026-10-16 13:07:30.746597","tipoBoletim":"Fechamento"}
        ]}
        """;

    private readonly FakePtax _ptax = new();

    [Fact]
    public async Task Sabado_usa_o_fechamento_de_sexta() {
        _ptax.Json = Week;

        var rate = await Rates().GetAsync("usd", "BRL", new DateOnly(2026, 10, 17));

        rate.Should().Be(new ExchangeRate("USD", "BRL", 4.98860m, 4.98920m,
            new DateTimeOffset(2026, 10, 16, 13, 7, 30, 746, TimeSpan.FromHours(-3)).AddTicks(5970), "PTAX"));
        _ptax.Url.Should().Contain("CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)")
            .And.Contain("@moeda='USD'").And.Contain("@dataInicial='10-07-2026'").And.Contain("@dataFinalCotacao='10-17-2026'");
    }

    [Fact]
    public async Task Fechamento_posterior_a_data_e_descartado() {
        _ptax.Json = Week;

        var rate = await Rates().GetAsync("USD", "BRL", new DateOnly(2026, 10, 15));

        rate.Sell.Should().Be(5.01190m);
        rate.Buy.Should().Be(5.01130m);
        rate.BaseCurrency.Should().Be("USD");
        rate.QuoteCurrency.Should().Be("BRL");
        rate.Source.Should().Be("PTAX");
        rate.QuotedAt.Offset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task Sem_fechamento_na_janela_lanca_indisponivel() {
        _ptax.Json = """{"value":[]}""";

        var act = () => Rates().GetAsync("USD", "BRL", new DateOnly(2026, 10, 17));

        await act.Should().ThrowAsync<ExchangeRateUnavailableException>().WithMessage("*USD/BRL*");
    }

    [Theory]
    [InlineData("BRL", "USD")]
    [InlineData("USD", "EUR")]
    [InlineData("BRL", "BRL")]
    public async Task Par_sem_BRL_como_cotada_nao_e_suportado(string from, string to) {
        var act = () => Rates().GetAsync(from, to, new DateOnly(2026, 10, 17));

        await act.Should().ThrowAsync<NotSupportedException>();
        _ptax.Url.Should().BeNull();
    }

    [Fact]
    public async Task Moeda_invalida_e_rejeitada() {
        var act = () => Rates().GetAsync("US$", "BRL", new DateOnly(2026, 10, 17));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Erro_http_propaga() {
        _ptax.Status = HttpStatusCode.ServiceUnavailable;

        var act = () => Rates().GetAsync("USD", "BRL", new DateOnly(2026, 10, 17));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Resposta_nula_lanca_indisponivel() {
        _ptax.Json = "null";

        var act = () => Rates().GetAsync("EUR", "BRL", new DateOnly(2026, 10, 17));

        await act.Should().ThrowAsync<ExchangeRateUnavailableException>();
    }

    [Fact]
    public void Builder_e_extensoes_exigem_argumentos() {
        ((Action)(() => _ = new ExchangeRatesBuilder(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ((IServiceCollection)null!).AddAedisExchangeRates())).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddAedisExchangeRates().WithPtax(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Url_que_nao_e_https_falha_na_validacao() {
        using var provider = Build(new Dictionary<string, string?> { ["ExchangeRates:Ptax:BaseUrl"] = "http://olinda.bcb.gov.br/" });

        var act = () => provider.GetRequiredService<IOptions<PtaxOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("HTTPS");
    }

    [Fact]
    public void Janela_fora_do_intervalo_falha_na_validacao() {
        using var provider = Build(new Dictionary<string, string?> { ["ExchangeRates:Ptax:LookbackDays"] = "0" });

        var act = () => provider.GetRequiredService<IOptions<PtaxOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Timeout_zero_falha_na_validacao() {
        using var provider = Build(new Dictionary<string, string?> { ["ExchangeRates:Ptax:Timeout"] = "00:00:00" });

        var act = () => provider.GetRequiredService<IOptions<PtaxOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Timeout");
    }

    [Fact]
    public void BaseUrl_sem_barra_final_ganha_a_barra() {
        using var provider = Build(new Dictionary<string, string?> { ["ExchangeRates:Ptax:BaseUrl"] = "https://ptax.test/odata" });

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(IExchangeRates).Name);

        http.BaseAddress.Should().Be(new Uri("https://ptax.test/odata/"));
    }

    [Fact]
    public void Registro_aponta_para_a_api_do_banco_central() {
        using var provider = Build(new Dictionary<string, string?>());

        provider.GetRequiredService<IExchangeRates>().Should().BeOfType<PtaxExchangeRates>();
        provider.GetRequiredService<IOptions<PtaxOptions>>().Value.BaseUrl.Should().StartWith("https://olinda.bcb.gov.br/");
    }

    private IExchangeRates Rates() {
        var services = new ServiceCollection();
        services.AddAedisExchangeRates().WithPtax(new ConfigurationBuilder().Build());
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _ptax));
        return services.BuildServiceProvider().GetRequiredService<IExchangeRates>();
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings) {
        var services = new ServiceCollection();
        services.AddAedisExchangeRates().WithPtax(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    private sealed class FakePtax : HttpMessageHandler
    {
        public string Json { get; set; } = """{"value":[]}""";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") });
        }
    }
}
