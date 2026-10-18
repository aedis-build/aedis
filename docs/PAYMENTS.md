# Aedis.Payments — cobrança recorrente

> Contrato para SaaS que cobra assinatura mensal e consumo avulso no cartão, e o primeiro provider, sobre a Stripe (SDK oficial `Stripe.net`).

## Pacotes

| Pacote | Tier | Papel |
|---|---|---|
| `Aedis.Payments.Abstractions` | 2 | `IPaymentGateway`, `IPaymentWebhookProcessor`, `IPaymentEventHandler`, `IPaymentEventStore` (+ `InMemoryPaymentEventStore`), valores (`PaymentRef`, `Money`, `PlanPrice`, `OneOffCharge`…), eventos (`PaymentSucceeded`, `PaymentFailed`, `SubscriptionChanged`, `SubscriptionCanceled`, `PaymentMethodUpdated`), `AddAedisPayments()` |
| `Aedis.Payments.Stripe` | 3 | Gateway e processador de webhook sobre a Stripe, várias contas por aplicação; `WithStripe(configuration)` |

O código de aplicação depende só de `Aedis.Payments.Abstractions`; a composition root escolhe o provider e registra o store e os handlers:

```csharp
services.AddAedisPayments().WithStripe(configuration);
services.AddScoped<IPaymentEventStore, MeuStoreEmTabela>();   // durável, chave única por evento
services.AddScoped<IPaymentEventHandler, ReguaDeCobranca>();
```

```json
{
  "Payments": {
    "Stripe": {
      "Accounts": {
        "br":     { "SecretKey": "<segredo>", "PublishableKey": "pk_test_…", "WebhookSecret": "<segredo>" },
        "global": { "SecretKey": "<segredo>", "PublishableKey": "pk_test_…", "WebhookSecret": "<segredo>", "AdaptivePricing": true }
      }
    }
  }
}
```

`SecretKey` e `WebhookSecret` chegam por um provider de segredos (`Aedis.Secrets.*`) ou variável de ambiente (`Payments__Stripe__Accounts__br__SecretKey`). A validação de subida exige as três chaves com o prefixo certo, recusa misturar teste e live e **recusa chave live** sem `Payments:Stripe:AllowLiveMode=true` — ligue só no ambiente de produção. As mensagens de erro citam a conta, nunca o valor da chave. `Payments:Stripe:ApiBase` aponta os clientes para o [stripe-mock](https://github.com/stripe/stripe-mock) em testes de integração.

## Várias contas

Cada entrada de `Accounts` é uma conta Stripe (ex.: uma conta Brasil, que cobra em BRL, e uma conta global com Adaptive Pricing, na mesma Organization). Ids da Stripe só valem dentro da conta que os criou, então tudo o que o gateway devolve é um `PaymentRef(Account, Id)`, e a aplicação guarda os dois junto do tenant. A conta é escolhida na criação do cliente (`NewCustomer.Account`) — por país da empresa, por exemplo — e todas as operações seguintes seguem a referência. O webhook descobre a conta pelo segredo que valida a assinatura, então um único endpoint serve todas.

## Ciclo

1. **Cliente** — `CreateCustomerAsync(new NewCustomer(account, tenantId, email, name))`. O tenant vai como metadado `aedis_tenant_id`; a chave de idempotência é por tenant, então repetir a chamada não duplica o cliente.
2. **Assinatura** — `CreateSubscriptionCheckoutAsync(new SubscriptionCheckout(customer, plan, successUrl, cancelUrl))` devolve a URL do Checkout hospedado, que coleta o cartão, cobra a primeira mensalidade e cria a assinatura. O resultado chega por webhook. O plano é:
   - `PlanPrice.Catalog("price_…")` — preço cadastrado. Na conta global com `AdaptivePricing`, a Stripe mostra e cobra na moeda do cliente e recalcula a cada ciclo;
   - `PlanPrice.Custom("prod_…", new Money(centavos, "BRL"))` — preço mensal montado na hora com um valor que a aplicação já converteu (ex.: plano em USD × PTAX do dia). Para reajustar o valor nos ciclos seguintes, chame `ChangePlanAsync` com o novo `Custom`.
3. **Troca de plano** — `ChangePlanAsync(subscription, plan)`: efeito imediato e `proration_behavior=always_invoice`, ou seja, a diferença proporcional ao resto do período é faturada e cobrada na hora; no rebaixamento, o crédito abate a próxima fatura. Também desfaz um cancelamento agendado. A assinatura precisa ter um único item.
4. **Cancelamento** — `CancelAtPeriodEndAsync(subscription)`: nada mais é cobrado e o acesso vai até `CurrentPeriodEnd`, quando chega `SubscriptionCanceled`.
5. **Troca de cartão** — `CreateCardSetupAsync(customer)` devolve o `ClientSecret` de um SetupIntent (`usage=off_session`) e a `PublishableKey` da conta do cliente, para o Payment Element do Stripe.js coletar o cartão sem ele passar pela aplicação. Quando o SetupIntent é confirmado, o webhook torna o cartão o padrão do cliente e emite `PaymentMethodUpdated`.
6. **Cobrança avulsa** — `ChargeAsync(new OneOffCharge(customer, "BRL", linhas, idempotencyKey))` cobra o cartão padrão sem o cliente presente. Recusa **não** lança exceção: volta `ChargeStatus.Failed` com o código (`insufficient_funds`…), ou `RequiresAction` se o banco pediu autenticação do portador. O teto por item é regra da aplicação: as linhas já chegam com o valor final.
7. **Retentativa** — `RetryChargeAsync(invoice)` paga de novo uma fatura em aberto (avulsa ou da assinatura) no cartão padrão atual; fatura que já estava paga volta `Paid`.

### Por que a cobrança avulsa é uma fatura

A Stripe tem dois caminhos para cobrar off-session um cartão salvo: um `PaymentIntent` solto ou uma `Invoice` com itens. O provider usa a fatura (criada com `auto_advance=false` e `pending_invoice_items_behavior=exclude`, um item por linha, finalizada e paga com `off_session=true`) porque:

- a fatura tem linhas, número, PDF e página hospedada — o cliente vê o que pagou e a contabilidade tem o documento;
- a mensalidade da assinatura já é uma fatura, então pagamento e falha chegam pelos **mesmos** eventos (`invoice.paid`, `invoice.payment_failed`) e a régua de cobrança é uma só;
- uma fatura em aberto pode ser retentada com `invoices/pay` (`RetryChargeAsync`) sem montar a cobrança de novo.

Cada passo leva uma chave de idempotência derivada de `IdempotencyKey` (`aedis:charge:{chave}:invoice`, `:item:{n}`, `:finalize`, `:pay`), então repetir a chamada depois de uma queda devolve a mesma fatura em vez de cobrar duas vezes. A Stripe guarda chaves de idempotência por 24 horas: depois disso, a mesma chave cria uma fatura nova — a aplicação registra a cobrança feita (`PaymentCharge.Invoice`) e não repete chamadas antigas.

## Webhook

```csharp
app.MapPost("/webhooks/pagamentos", async (HttpRequest request, IPaymentWebhookProcessor processor, CancellationToken ct) => {
    var payload = await new StreamReader(request.Body).ReadToEndAsync(ct);
    try {
        await processor.ProcessAsync(payload, request.Headers[StripePaymentsBuilderExtensions.SignatureHeader].ToString(), ct);
        return Results.Ok();
    } catch (PaymentWebhookSignatureException) {
        return Results.BadRequest();
    }
}).AllowAnonymous();
```

- **Assinatura, falha fechado.** O cabeçalho `Stripe-Signature` é conferido contra o `WebhookSecret` de cada conta, com tolerância de 5 minutos. Sem acerto (segredo errado, corpo adulterado, assinatura velha, cabeçalho ausente), `PaymentWebhookSignatureException` e nada é processado. Leia o corpo cru: a assinatura cobre os bytes exatos.
- **Idempotência.** O evento é reservado no `IPaymentEventStore` (`stripe:{conta}:{evt_…}`) antes dos handlers e concluído só depois de todos passarem. Reenvio de evento concluído volta `WebhookOutcome.Duplicate` sem chamar handler; exceção de handler libera a reserva e propaga (responda 5xx), e a Stripe reenvia. Sem `IPaymentEventStore` registrado o processador recusa a notificação. `InMemoryPaymentEventStore` serve a testes e a uma instância única; em produção, use uma tabela com chave única e o mesmo contrato (reserva com prazo, conclusão, liberação).
- **Versão da API.** Crie os endpoints de webhook com a versão da API do `Stripe.net` em uso (`StripeConfiguration.ApiVersion`); evento de outra versão é rejeitado na leitura.
- **Eventos traduzidos** (cada handler recebe um `PaymentEvent` num escopo de DI por notificação):

| Stripe | Evento | Para quê |
|---|---|---|
| `invoice.paid` | `PaymentSucceeded` | liberar o período, encerrar a régua |
| `invoice.payment_failed` | `PaymentFailed` (`AttemptCount`, `NextAttemptAt`) | começar ou avançar a régua de cobrança |
| `customer.subscription.created` / `.updated` | `SubscriptionChanged` | ativação, troca de plano, atraso, cancelamento agendado |
| `customer.subscription.deleted` | `SubscriptionCanceled` | fim do acesso |
| `checkout.session.completed` (assinatura) / `setup_intent.succeeded` | `PaymentMethodUpdated` | cartão novo: tentar de novo faturas em aberto |

  Os demais tipos voltam `WebhookOutcome.Ignored`. No `checkout.session.completed`, o cartão que o Checkout gravou na assinatura passa a ser o padrão do cliente e sai da assinatura — assim mensalidade, cobrança avulsa e troca de cartão usam a mesma fonte.
- **Ordem.** A Stripe não garante a ordem de entrega. Os eventos de assinatura trazem a fotografia completa; em dúvida, leia o estado atual com `GetSubscriptionAsync`.

## Régua de cobrança

O módulo entrega os fatos; a política é da aplicação. Para uma régua própria (ex.: tentar de novo na sexta e no sábado e pausar o serviço no domingo):

1. Desligue as retentativas automáticas da Stripe para as faturas da assinatura (configuração de retentativas do Billing no Dashboard de cada conta) para não haver duas réguas. Faturas avulsas já nascem sem retentativa automática (`auto_advance=false`).
2. Em `PaymentFailed`, registre a fatura em aberto e agende as tentativas.
3. No dia da tentativa, chame `RetryChargeAsync(invoice)`; em `PaymentMethodUpdated`, tente as faturas em aberto do cliente na hora.
4. Em `PaymentSucceeded`, encerre a régua da fatura e reative o que tiver sido pausado.

## Testes

O provider fala com a Stripe só pelos `StripeClient` de cada conta, sem o estado global `StripeConfiguration`. Os testes do pacote trocam o transporte HTTP por um fake e assinam payloads com `EventUtility.GenerateSignatureHeader`; para testes de integração, aponte `Payments:Stripe:ApiBase` para um stripe-mock. Use sempre chaves `sk_test_`/`pk_test_` fora de produção — a validação de subida garante isso.
