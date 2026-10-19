using Aedis.Payments.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Payments.Stripe.Tests;

/// <summary>
///     Registro e validação: <c>AddAedisPayments().WithStripe(configuration)</c> vincula <c>Payments:Stripe</c>, resolve
///     gateway e processador, e a validação barra configuração incompleta e chave live sem <c>AllowLiveMode</c> — sem
///     ecoar o valor da chave.
/// </summary>
public class StripeRegistrationTests
{
    [Fact]
    public void Registra_gateway_e_processador_com_duas_contas() {
        using var provider = Build(Valid());

        provider.GetRequiredService<IPaymentGateway>().Should().BeOfType<StripePaymentGateway>();
        provider.GetRequiredService<IPaymentWebhookProcessor>().Should().BeOfType<StripeWebhookProcessor>();
        provider.GetRequiredService<StripeAccounts>().All.Select(a => a.Name).Should().BeEquivalentTo("br", "global");
        provider.GetRequiredService<IOptions<StripeOptions>>().Value.Accounts["global"].AdaptivePricing.Should().BeTrue();
    }

    [Fact]
    public void Chave_live_sem_AllowLiveMode_falha_sem_vazar_a_chave() {
        var settings = Valid();
        settings["Payments:Stripe:Accounts:br:SecretKey"] = "sk_live_segredo";
        settings["Payments:Stripe:Accounts:br:PublishableKey"] = "pk_live_x";
        using var provider = Build(settings);

        var act = () => provider.GetRequiredService<IOptions<StripeOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("AllowLiveMode").And.NotContain("sk_live_segredo");
    }

    [Fact]
    public void Chave_live_com_AllowLiveMode_e_aceita() {
        var settings = Valid();
        settings["Payments:Stripe:AllowLiveMode"] = "true";
        settings["Payments:Stripe:Accounts:br:SecretKey"] = "sk_live_x";
        settings["Payments:Stripe:Accounts:br:PublishableKey"] = "pk_live_x";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<StripeOptions>>().Value.AllowLiveMode.Should().BeTrue();
    }

    [Theory]
    [InlineData("Payments:Stripe:Accounts:br:SecretKey", "pk_test_x", "SecretKey")]
    [InlineData("Payments:Stripe:Accounts:br:PublishableKey", "", "PublishableKey")]
    [InlineData("Payments:Stripe:Accounts:br:WebhookSecret", "segredo", "WebhookSecret")]
    [InlineData("Payments:Stripe:Accounts:br:PublishableKey", "pk_live_x", "mistura")]
    [InlineData("Payments:Stripe:ApiBase", "nao-e-url", "ApiBase")]
    public void Configuracao_invalida_falha_na_validacao(string key, string value, string expected) {
        var settings = Valid();
        settings[key] = value;
        using var provider = Build(settings);

        var act = () => provider.GetRequiredService<IOptions<StripeOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(expected);
    }

    [Fact]
    public void Sem_contas_falha_na_validacao() {
        using var provider = Build(new Dictionary<string, string?>());

        var act = () => provider.GetRequiredService<IOptions<StripeOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("ao menos uma conta");
    }

    [Fact]
    public void ApiBase_aponta_os_clientes_para_o_stripe_mock() {
        var settings = Valid();
        settings["Payments:Stripe:ApiBase"] = "http://localhost:12111/";
        using var provider = Build(settings);

        provider.GetRequiredService<StripeAccounts>().Get("br").Client.ApiBase.Should().Be("http://localhost:12111");
    }

    private static Dictionary<string, string?> Valid() => new() {
        ["Payments:Stripe:Accounts:br:SecretKey"] = "sk_test_br",
        ["Payments:Stripe:Accounts:br:PublishableKey"] = "pk_test_br",
        ["Payments:Stripe:Accounts:br:WebhookSecret"] = "whsec_br",
        ["Payments:Stripe:Accounts:global:SecretKey"] = "rk_test_global",
        ["Payments:Stripe:Accounts:global:PublishableKey"] = "pk_test_global",
        ["Payments:Stripe:Accounts:global:WebhookSecret"] = "whsec_global",
        ["Payments:Stripe:Accounts:global:AdaptivePricing"] = "true",
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAedisPayments().WithStripe(configuration);
        return services.BuildServiceProvider();
    }
}
