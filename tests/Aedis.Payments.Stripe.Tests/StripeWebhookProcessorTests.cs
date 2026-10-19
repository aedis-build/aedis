using Aedis.Payments.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Xunit;

namespace Aedis.Payments.Stripe.Tests;

/// <summary>
///     Webhook da Stripe: assinatura conferida contra o segredo de cada conta (falha fechado), evento traduzido para o
///     tipo do contrato e entregue aos handlers uma única vez — duplicata não chega ao handler, e falha de handler
///     libera o evento para o reenvio.
/// </summary>
public class StripeWebhookProcessorTests
{
    private readonly FakeStripeApi _api = new();
    private readonly RecordingHandler _handler = new();
    private readonly ServiceCollection _services = new();

    public StripeWebhookProcessorTests() {
        _services.AddSingleton<IPaymentEventStore>(new InMemoryPaymentEventStore());
        _services.AddSingleton<IPaymentEventHandler>(_handler);
    }

    [Fact]
    public async Task Fatura_paga_vira_PaymentSucceeded_na_conta_que_assinou() {
        var payload = Event("evt_1", EventTypes.InvoicePaid, """
            {"id":"in_1","object":"invoice","customer":"cus_1","amount_paid":10000,"currency":"brl",
             "parent":{"type":"subscription_details","subscription_details":{"subscription":"sub_1"}}}
            """);

        var outcome = await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.GlobalWebhookSecret));

        outcome.Should().Be(WebhookOutcome.Processed);
        _handler.Events.Single().Should().BeEquivalentTo(new PaymentSucceeded("global", "evt_1", DateTimeOffset.FromUnixTimeSeconds(1760000000),
            new PaymentRef("global", "in_1"), new PaymentRef("global", "cus_1"), "sub_1", new Money(10000, "BRL")));
    }

    [Fact]
    public async Task Falha_de_pagamento_vira_PaymentFailed_com_tentativas() {
        var payload = Event("evt_2", EventTypes.InvoicePaymentFailed, """
            {"id":"in_2","object":"invoice","customer":"cus_1","amount_due":3500,"currency":"brl","attempt_count":2,
             "next_payment_attempt":1760086400}
            """);

        await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        var failed = _handler.Events.Single().Should().BeOfType<PaymentFailed>().Subject;
        failed.Account.Should().Be("br");
        failed.Invoice.Should().Be(new PaymentRef("br", "in_2"));
        failed.Customer.Should().Be(new PaymentRef("br", "cus_1"));
        failed.SubscriptionId.Should().BeNull();
        failed.AmountDue.Should().Be(new Money(3500, "BRL"));
        failed.AttemptCount.Should().Be(2);
        failed.NextAttemptAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1760086400));
    }

    [Theory]
    [InlineData("customer.subscription.created", typeof(SubscriptionChanged))]
    [InlineData("customer.subscription.updated", typeof(SubscriptionChanged))]
    [InlineData("customer.subscription.deleted", typeof(SubscriptionCanceled))]
    public async Task Eventos_de_assinatura_sao_traduzidos(string type, Type expected) {
        var payload = Event("evt_s", type, """
            {"id":"sub_1","object":"subscription","customer":"cus_1","status":"canceled","cancel_at_period_end":true,
             "items":{"object":"list","data":[{"id":"si_1","object":"subscription_item","current_period_end":1790000000,"price":{"id":"price_pro","object":"price"}}]}}
            """);

        await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        var paymentEvent = _handler.Events.Single();
        paymentEvent.Should().BeOfType(expected);
        var subscription = paymentEvent is SubscriptionChanged changed ? changed.Subscription : ((SubscriptionCanceled)paymentEvent).Subscription;
        subscription.Should().Be(new PaymentSubscription(new PaymentRef("br", "sub_1"), new PaymentRef("br", "cus_1"),
            SubscriptionStatus.Canceled, "price_pro", DateTimeOffset.FromUnixTimeSeconds(1790000000), true));
    }

    [Fact]
    public async Task Cartao_confirmado_vira_padrao_do_cliente() {
        _api.Respond("POST /v1/customers/cus_1", """{"id":"cus_1","object":"customer"}""");
        var payload = Event("evt_3", EventTypes.SetupIntentSucceeded,
            """{"id":"seti_1","object":"setup_intent","customer":"cus_1","payment_method":"pm_novo","usage":"off_session"}""");

        await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        _api.Requests.Single().Form.Should().Contain("invoice_settings[default_payment_method]", "pm_novo");
        _handler.Events.Single().Should().BeEquivalentTo(new PaymentMethodUpdated("br", "evt_3", DateTimeOffset.FromUnixTimeSeconds(1760000000),
            new PaymentRef("br", "cus_1"), "pm_novo"));
    }

    [Fact]
    public async Task Checkout_concluido_passa_o_cartao_da_assinatura_para_o_cliente() {
        _api.Respond("GET /v1/subscriptions/sub_1", """{"id":"sub_1","object":"subscription","customer":"cus_1","default_payment_method":"pm_1"}""")
            .Respond("POST /v1/customers/cus_1", """{"id":"cus_1","object":"customer"}""")
            .Respond("POST /v1/subscriptions/sub_1", """{"id":"sub_1","object":"subscription","customer":"cus_1"}""");
        var payload = Event("evt_4", EventTypes.CheckoutSessionCompleted,
            """{"id":"cs_1","object":"checkout.session","mode":"subscription","customer":"cus_1","subscription":"sub_1"}""");

        await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        _api.Requests.Select(r => r.Route).Should().Equal("GET /v1/subscriptions/sub_1", "POST /v1/customers/cus_1", "POST /v1/subscriptions/sub_1");
        _api.Requests[1].Form.Should().Contain("invoice_settings[default_payment_method]", "pm_1");
        _api.Requests[2].Form.Should().Contain("default_payment_method", string.Empty);
        _handler.Events.Single().Should().BeOfType<PaymentMethodUpdated>().Which.PaymentMethodId.Should().Be("pm_1");
    }

    [Fact]
    public async Task Checkout_sem_cartao_na_assinatura_e_ignorado() {
        _api.Respond("GET /v1/subscriptions/sub_1", """{"id":"sub_1","object":"subscription","customer":"cus_1"}""");
        var payload = Event("evt_5", EventTypes.CheckoutSessionCompleted,
            """{"id":"cs_1","object":"checkout.session","mode":"subscription","customer":"cus_1","subscription":"sub_1"}""");

        (await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret))).Should().Be(WebhookOutcome.Ignored);
    }

    [Theory]
    [InlineData("checkout.session.completed", """{"id":"cs_1","object":"checkout.session","mode":"payment","customer":"cus_1"}""")]
    [InlineData("setup_intent.succeeded", """{"id":"seti_1","object":"setup_intent","payment_method":"pm_1"}""")]
    public async Task Evento_sem_os_dados_esperados_e_ignorado_sem_chamar_a_api(string type, string dataObject) {
        var payload = Event("evt_x", type, dataObject);

        (await Processor().ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret))).Should().Be(WebhookOutcome.Ignored);
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Tipo_nao_traduzido_e_ignorado_e_marcado() {
        var payload = Event("evt_6", "charge.refunded", """{"id":"ch_1","object":"charge"}""");
        var processor = Processor();

        (await processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret))).Should().Be(WebhookOutcome.Ignored);
        (await processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret))).Should().Be(WebhookOutcome.Duplicate);
        _handler.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Reenvio_do_mesmo_evento_nao_chega_ao_handler() {
        var payload = PaidEvent("evt_7");
        var processor = Processor();

        await processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));
        var second = await processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        second.Should().Be(WebhookOutcome.Duplicate);
        _handler.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Falha_do_handler_libera_o_evento_para_o_reenvio() {
        var payload = PaidEvent("evt_8");
        var processor = Processor();
        _handler.FailNext = true;

        var first = () => processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));
        await first.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler caiu");
        var retry = await processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        retry.Should().Be(WebhookOutcome.Processed);
        _handler.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Assinatura_de_segredo_desconhecido_falha_fechado() {
        var payload = PaidEvent("evt_9");

        var act = () => Processor().ProcessAsync(payload, Sign(payload, "whsec_outro"));

        await act.Should().ThrowAsync<PaymentWebhookSignatureException>();
        _handler.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Corpo_adulterado_falha_fechado() {
        var payload = PaidEvent("evt_10");
        var signature = Sign(payload, TestAccounts.BrWebhookSecret);

        var act = () => Processor().ProcessAsync(payload.Replace("10000", "1"), signature);

        await act.Should().ThrowAsync<PaymentWebhookSignatureException>();
    }

    [Fact]
    public async Task Assinatura_antiga_falha_fechado() {
        var payload = PaidEvent("evt_11");
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        var act = () => Processor().ProcessAsync(payload, EventUtility.GenerateSignatureHeader(payload, TestAccounts.BrWebhookSecret, old));

        await act.Should().ThrowAsync<PaymentWebhookSignatureException>();
    }

    [Theory]
    [InlineData("", "t=1,v1=x")]
    [InlineData("{}", "")]
    public async Task Sem_corpo_ou_sem_cabecalho_falha_fechado(string payload, string signature) {
        var act = () => Processor().ProcessAsync(payload, signature);

        await act.Should().ThrowAsync<PaymentWebhookSignatureException>();
    }

    [Fact]
    public async Task Sem_store_registrado_recusa_processar() {
        var services = new ServiceCollection();
        services.AddSingleton<IPaymentEventHandler>(_handler);
        var processor = new StripeWebhookProcessor(_api.Accounts(TestAccounts.Options()),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<StripeWebhookProcessor>.Instance);
        var payload = PaidEvent("evt_12");

        var act = () => processor.ProcessAsync(payload, Sign(payload, TestAccounts.BrWebhookSecret));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IPaymentEventStore*");
        _handler.Events.Should().BeEmpty();
    }

    private StripeWebhookProcessor Processor() => new(_api.Accounts(TestAccounts.Options()),
        _services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<StripeWebhookProcessor>.Instance);

    private static string PaidEvent(string id) => Event(id, EventTypes.InvoicePaid,
        """{"id":"in_1","object":"invoice","customer":"cus_1","amount_paid":10000,"currency":"brl"}""");

    private static string Event(string id, string type, string dataObject) =>
        $$"""{"id":"{{id}}","object":"event","api_version":"{{StripeConfiguration.ApiVersion}}","created":1760000000,"livemode":false,"type":"{{type}}","data":{"object":""" + dataObject + "}}";

    private static string Sign(string payload, string secret) => EventUtility.GenerateSignatureHeader(payload, secret);

    private sealed class RecordingHandler : IPaymentEventHandler
    {
        public List<PaymentEvent> Events { get; } = [];

        public bool FailNext { get; set; }

        public Task HandleAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken) {
            if (FailNext) {
                FailNext = false;
                throw new InvalidOperationException("handler caiu");
            }

            Events.Add(paymentEvent);
            return Task.CompletedTask;
        }
    }
}
