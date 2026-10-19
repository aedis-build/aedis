using System.Net;
using Aedis.Payments.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Payments.Stripe.Tests;

/// <summary>
///     <see cref="StripePaymentGateway" /> contra a API fake: cada operação usa a chave da conta do cliente, manda os
///     parâmetros que a Stripe espera (proporcional cobrado na hora, cancelamento no fim do período, fatura avulsa
///     off-session com chaves de idempotência por passo) e traduz recusa de cartão em resultado, não em exceção.
/// </summary>
public class StripePaymentGatewayTests
{
    private const string Subscription = """
        {"id":"sub_1","object":"subscription","customer":"cus_1","status":"active","cancel_at_period_end":false,
         "items":{"object":"list","data":[{"id":"si_1","object":"subscription_item","current_period_end":1790000000,
         "price":{"id":"price_pro","object":"price"}}]}}
        """;

    private readonly FakeStripeApi _api = new();
    private readonly StripePaymentGateway _gateway;

    public StripePaymentGatewayTests() => _gateway = new StripePaymentGateway(_api.Accounts(TestAccounts.Options()));

    [Fact]
    public async Task Cria_cliente_na_conta_pedida_com_tenant_e_idempotencia() {
        _api.Respond("POST /v1/customers", """{"id":"cus_9","object":"customer"}""");

        var customer = await _gateway.CreateCustomerAsync(new NewCustomer("global", "tenant-1", "fin@acme.com", "Acme"));

        customer.Should().Be(new PaymentRef("global", "cus_9"));
        var request = _api.Requests.Single();
        request.ApiKey.Should().Be("sk_test_global");
        request.IdempotencyKey.Should().Be("aedis:customer:tenant-1");
        request.Form.Should().Contain("metadata[aedis_tenant_id]", "tenant-1").And.Contain("email", "fin@acme.com");
    }

    [Fact]
    public async Task Conta_desconhecida_e_rejeitada() {
        var act = () => _gateway.CreateCustomerAsync(new NewCustomer("eu", "t", "a@b.com"));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*'eu'*");
    }

    [Fact]
    public async Task Checkout_com_preco_montado_e_adaptive_pricing() {
        _api.Respond("POST /v1/checkout/sessions", """{"id":"cs_1","object":"checkout.session","url":"https://checkout.stripe.com/c/cs_1"}""");

        var url = await _gateway.CreateSubscriptionCheckoutAsync(new SubscriptionCheckout(
            new PaymentRef("global", "cus_1"), PlanPrice.Custom("prod_pro", new Money(50000, "brl")),
            new Uri("https://app.test/ok"), new Uri("https://app.test/volta")));

        url.Should().Be(new Uri("https://checkout.stripe.com/c/cs_1"));
        _api.Requests.Single().Form.Should()
            .Contain("mode", "subscription")
            .And.Contain("customer", "cus_1")
            .And.Contain("line_items[0][price_data][product]", "prod_pro")
            .And.Contain("line_items[0][price_data][currency]", "brl")
            .And.Contain("line_items[0][price_data][unit_amount]", "50000")
            .And.Contain("line_items[0][price_data][recurring][interval]", "month")
            .And.Contain("adaptive_pricing[enabled]", "true");
    }

    [Fact]
    public async Task Checkout_com_preco_do_catalogo_sem_adaptive_pricing_na_conta_br() {
        _api.Respond("POST /v1/checkout/sessions", """{"id":"cs_1","object":"checkout.session","url":"https://checkout.stripe.com/c/cs_1"}""");

        await _gateway.CreateSubscriptionCheckoutAsync(new SubscriptionCheckout(
            new PaymentRef("br", "cus_1"), PlanPrice.Catalog("price_pro"), new Uri("https://app.test/ok"), new Uri("https://app.test/volta")));

        var form = _api.Requests.Single().Form;
        form.Should().Contain("line_items[0][price]", "price_pro");
        form.Keys.Should().NotContain(k => k.StartsWith("adaptive_pricing"));
    }

    [Fact]
    public async Task Troca_de_plano_cobra_o_proporcional_na_hora_e_desfaz_cancelamento() {
        _api.Respond("GET /v1/subscriptions/sub_1", Subscription)
            .Respond("POST /v1/subscriptions/sub_1", Subscription.Replace("price_pro", "price_max"));

        var result = await _gateway.ChangePlanAsync(new PaymentRef("br", "sub_1"), PlanPrice.Catalog("price_max"));

        result.PriceId.Should().Be("price_max");
        result.Status.Should().Be(SubscriptionStatus.Active);
        result.Customer.Should().Be(new PaymentRef("br", "cus_1"));
        result.CurrentPeriodEnd.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1790000000));
        _api.Requests[1].Form.Should()
            .Contain("items[0][id]", "si_1")
            .And.Contain("items[0][price]", "price_max")
            .And.Contain("proration_behavior", "always_invoice")
            .And.Contain("cancel_at_period_end", "false");
    }

    [Fact]
    public async Task Troca_de_plano_com_preco_montado_usa_price_data() {
        _api.Respond("GET /v1/subscriptions/sub_1", Subscription).Respond("POST /v1/subscriptions/sub_1", Subscription);

        await _gateway.ChangePlanAsync(new PaymentRef("br", "sub_1"), PlanPrice.Custom("prod_max", new Money(100000, "BRL")));

        _api.Requests[1].Form.Should()
            .Contain("items[0][price_data][product]", "prod_max")
            .And.Contain("items[0][price_data][unit_amount]", "100000")
            .And.Contain("items[0][price_data][recurring][interval]", "month");
    }

    [Fact]
    public async Task Troca_de_plano_recusa_assinatura_com_varios_itens() {
        _api.Respond("GET /v1/subscriptions/sub_1", """
            {"id":"sub_1","object":"subscription","customer":"cus_1","items":{"object":"list","data":[
             {"id":"si_1","object":"subscription_item"},{"id":"si_2","object":"subscription_item"}]}}
            """);

        var act = () => _gateway.ChangePlanAsync(new PaymentRef("br", "sub_1"), PlanPrice.Catalog("price_max"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*2 itens*");
    }

    [Fact]
    public async Task Cancelamento_fica_para_o_fim_do_periodo() {
        _api.Respond("POST /v1/subscriptions/sub_1", Subscription.Replace("\"cancel_at_period_end\":false", "\"cancel_at_period_end\":true"));

        var result = await _gateway.CancelAtPeriodEndAsync(new PaymentRef("br", "sub_1"));

        result.CancelAtPeriodEnd.Should().BeTrue();
        _api.Requests.Single().Form.Should().Contain("cancel_at_period_end", "true");
    }

    [Fact]
    public async Task Le_a_assinatura() {
        _api.Respond("GET /v1/subscriptions/sub_1", Subscription.Replace("active", "past_due"));

        var result = await _gateway.GetSubscriptionAsync(new PaymentRef("br", "sub_1"));

        result.Status.Should().Be(SubscriptionStatus.PastDue);
        result.Subscription.Should().Be(new PaymentRef("br", "sub_1"));
    }

    [Fact]
    public async Task Coleta_de_cartao_devolve_segredo_e_chave_publica_da_conta() {
        _api.Respond("POST /v1/setup_intents", """{"id":"seti_1","object":"setup_intent","client_secret":"seti_1_secret_x"}""");

        var setup = await _gateway.CreateCardSetupAsync(new PaymentRef("br", "cus_1"));

        setup.Should().Be(new CardSetup("seti_1_secret_x", "pk_test_br"));
        _api.Requests.Single().Form.Should().Contain("usage", "off_session").And.Contain("customer", "cus_1");
    }

    [Fact]
    public async Task Cobranca_avulsa_e_fatura_com_itens_finalizada_e_paga_off_session() {
        RespondInvoiceFlow("""{"id":"in_1","object":"invoice","status":"paid","total":3500,"currency":"brl"}""");

        var charge = await _gateway.ChargeAsync(new OneOffCharge(new PaymentRef("br", "cus_1"), "BRL",
            [new ChargeLine("Ana — semana 42", 2000), new ChargeLine("Bia — semana 42", 1500)], "prod:t1:2026-W42", "Produtividade"));

        charge.Should().Be(new PaymentCharge(new PaymentRef("br", "in_1"), new Money(3500, "BRL"), ChargeStatus.Paid));
        _api.Requests.Select(r => r.Route).Should().Equal(
            "POST /v1/invoices", "POST /v1/invoiceitems", "POST /v1/invoiceitems", "POST /v1/invoices/in_1/finalize", "POST /v1/invoices/in_1/pay");
        _api.Requests.Select(r => r.IdempotencyKey).Should().Equal(
            "aedis:charge:prod:t1:2026-W42:invoice", "aedis:charge:prod:t1:2026-W42:item:0", "aedis:charge:prod:t1:2026-W42:item:1",
            "aedis:charge:prod:t1:2026-W42:finalize", "aedis:charge:prod:t1:2026-W42:pay");
        _api.Requests[0].Form.Should()
            .Contain("currency", "brl")
            .And.Contain("collection_method", "charge_automatically")
            .And.Contain("auto_advance", "false")
            .And.Contain("pending_invoice_items_behavior", "exclude")
            .And.Contain("metadata[aedis_idempotency_key]", "prod:t1:2026-W42");
        _api.Requests[1].Form.Should().Contain("invoice", "in_1").And.Contain("amount", "2000").And.Contain("description", "Ana — semana 42");
        _api.Requests[4].Form.Should().Contain("off_session", "true");
    }

    [Fact]
    public async Task Cartao_recusado_vira_falha_com_codigo() {
        RespondInvoiceFlow("""{"error":{"type":"card_error","code":"card_declined","decline_code":"insufficient_funds","message":"Saldo insuficiente."}}""",
            HttpStatusCode.PaymentRequired);
        _api.Respond("GET /v1/invoices/in_1", """{"id":"in_1","object":"invoice","status":"open","total":2000,"currency":"brl"}""");

        var charge = await _gateway.ChargeAsync(Charge());

        charge.Status.Should().Be(ChargeStatus.Failed);
        charge.FailureCode.Should().Be("insufficient_funds");
        charge.FailureMessage.Should().Be("Saldo insuficiente.");
        charge.Invoice.Should().Be(new PaymentRef("br", "in_1"));
    }

    [Fact]
    public async Task Autenticacao_exigida_vira_requires_action() {
        RespondInvoiceFlow("""{"error":{"type":"card_error","code":"authentication_required","message":"3DS."}}""", HttpStatusCode.PaymentRequired);
        _api.Respond("GET /v1/invoices/in_1", """{"id":"in_1","object":"invoice","status":"open","total":2000,"currency":"brl"}""");

        var charge = await _gateway.ChargeAsync(Charge());

        charge.Status.Should().Be(ChargeStatus.RequiresAction);
        charge.FailureCode.Should().Be("authentication_required");
        charge.FailureMessage.Should().Be("3DS.");
    }

    [Fact]
    public async Task Pagamento_em_processamento_vira_pending() {
        RespondInvoiceFlow("""{"id":"in_1","object":"invoice","status":"open","total":2000,"currency":"brl"}""");

        (await _gateway.ChargeAsync(Charge())).Status.Should().Be(ChargeStatus.Pending);
    }

    [Fact]
    public async Task Retentativa_de_fatura_ja_paga_devolve_paga() {
        _api.Respond("POST /v1/invoices/in_1/pay", """{"error":{"type":"invalid_request_error","message":"Invoice is already paid"}}""", HttpStatusCode.BadRequest)
            .Respond("GET /v1/invoices/in_1", """{"id":"in_1","object":"invoice","status":"paid","total":2000,"currency":"brl"}""");

        var charge = await _gateway.RetryChargeAsync(new PaymentRef("br", "in_1"));

        charge.Status.Should().Be(ChargeStatus.Paid);
        _api.Requests[0].IdempotencyKey.Should().NotStartWith("aedis:");
    }

    [Fact]
    public async Task Erro_de_requisicao_em_fatura_aberta_propaga() {
        _api.Respond("POST /v1/invoices/in_1/pay", """{"error":{"type":"invalid_request_error","message":"No payment method"}}""", HttpStatusCode.BadRequest)
            .Respond("GET /v1/invoices/in_1", """{"id":"in_1","object":"invoice","status":"open","total":2000,"currency":"brl"}""");

        var act = () => _gateway.RetryChargeAsync(new PaymentRef("br", "in_1"));

        await act.Should().ThrowAsync<global::Stripe.StripeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Linha_sem_valor_positivo_e_rejeitada_antes_da_api(long amount) {
        var act = () => _gateway.ChargeAsync(Charge() with { Lines = [new ChargeLine("x", amount)] });

        await act.Should().ThrowAsync<ArgumentException>();
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Cobranca_sem_linhas_ou_sem_chave_e_rejeitada() {
        await ((Func<Task>)(() => _gateway.ChargeAsync(Charge() with { Lines = [] }))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => _gateway.ChargeAsync(Charge() with { IdempotencyKey = " " }))).Should().ThrowAsync<ArgumentException>();
        _api.Requests.Should().BeEmpty();
    }

    private static OneOffCharge Charge() => new(new PaymentRef("br", "cus_1"), "BRL", [new ChargeLine("Ana", 2000)], "k1");

    private void RespondInvoiceFlow(string payResponse, HttpStatusCode payStatus = HttpStatusCode.OK) {
        const string draft = """{"id":"in_1","object":"invoice","status":"draft","total":0,"currency":"brl"}""";
        _api.Respond("POST /v1/invoices", draft)
            .Respond("POST /v1/invoiceitems", """{"id":"ii_1","object":"invoiceitem"}""")
            .Respond("POST /v1/invoices/in_1/finalize", draft.Replace("draft", "open"))
            .Respond("POST /v1/invoices/in_1/pay", payResponse, payStatus);
    }
}
