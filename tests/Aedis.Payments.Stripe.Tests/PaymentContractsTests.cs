using Aedis.Payments.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Aedis.Payments.Stripe.Tests;

/// <summary>
///     Contratos de <c>Aedis.Payments.Abstractions</c>: valores validados na fronteira (moeda ISO, valor não negativo,
///     referência completa, preço de plano positivo) e o store em memória com reserva, conclusão, liberação e
///     expiração da reserva.
/// </summary>
public class PaymentContractsTests
{
    [Fact]
    public void Money_normaliza_a_moeda() => new Money(100, " brl ").Currency.Should().Be("BRL");

    [Theory]
    [InlineData("R$")]
    [InlineData("REAL")]
    [InlineData("")]
    public void Money_recusa_moeda_invalida(string currency) =>
        ((Action)(() => _ = new Money(1, currency))).Should().Throw<ArgumentException>();

    [Fact]
    public void Money_recusa_valor_negativo() =>
        ((Action)(() => _ = new Money(-1, "BRL"))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void PaymentRef_exige_conta_e_id() {
        ((Action)(() => _ = new PaymentRef("", "cus_1"))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new PaymentRef("br", " "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PlanPrice_custom_exige_valor_positivo() {
        ((Action)(() => PlanPrice.Custom("prod", new Money(0, "BRL")))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PlanPrice.Catalog(""))).Should().Throw<ArgumentException>();
        PlanPrice.Custom("prod", new Money(1, "BRL")).PriceId.Should().BeNull();
    }

    [Fact]
    public void Builder_e_extensoes_exigem_argumentos() {
        ((Action)(() => _ = new PaymentsBuilder(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ((IServiceCollection)null!).AddAedisPayments())).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddAedisPayments().WithStripe(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddAedisPayments_devolve_o_builder() {
        var services = new ServiceCollection();

        services.AddAedisPayments().Services.Should().BeSameAs(services);
    }

    [Theory]
    [InlineData("incomplete", SubscriptionStatus.Incomplete)]
    [InlineData("incomplete_expired", SubscriptionStatus.IncompleteExpired)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.Unpaid)]
    [InlineData("paused", SubscriptionStatus.Paused)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("algo_novo", SubscriptionStatus.Unknown)]
    [InlineData(null, SubscriptionStatus.Unknown)]
    public void Status_da_Stripe_e_normalizado(string? status, SubscriptionStatus expected) =>
        StripeMapper.ToStatus(status).Should().Be(expected);

    [Fact]
    public void Assinatura_sem_itens_nao_tem_preco_nem_periodo() {
        var subscription = StripeMapper.ToSubscription("br", new global::Stripe.Subscription { Id = "sub_1", CustomerId = "cus_1", Status = "active" });

        subscription.PriceId.Should().BeNull();
        subscription.CurrentPeriodEnd.Should().BeNull();
    }

    [Fact]
    public async Task Store_em_memoria_reserva_conclui_e_libera() {
        var store = new InMemoryPaymentEventStore();

        (await store.TryClaimAsync("e1", default)).Should().BeTrue();
        (await store.TryClaimAsync("e1", default)).Should().BeFalse();
        await store.ReleaseAsync("e1", default);
        (await store.TryClaimAsync("e1", default)).Should().BeTrue();
        await store.CompleteAsync("e1", default);
        (await store.TryClaimAsync("e1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Reserva_esquecida_expira_mas_conclusao_nao() {
        var time = new FakeTimeProvider();
        var store = new InMemoryPaymentEventStore(time);
        await store.TryClaimAsync("orfao", default);
        await store.TryClaimAsync("feito", default);
        await store.CompleteAsync("feito", default);

        time.Advance(InMemoryPaymentEventStore.ClaimTimeout);

        (await store.TryClaimAsync("orfao", default)).Should().BeTrue();
        (await store.TryClaimAsync("feito", default)).Should().BeFalse();
    }
}
