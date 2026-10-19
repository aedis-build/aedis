# Aedis.ExchangeRates — cotação de câmbio

> Contrato de cotação por data e o primeiro provider, sobre a PTAX do Banco Central do Brasil.

## Pacotes

| Pacote | Tier | Papel |
|---|---|---|
| `Aedis.ExchangeRates.Abstractions` | 2 | `IExchangeRates`, `ExchangeRate` (compra, venda, instante, fonte), `ExchangeRateUnavailableException`, `AddAedisExchangeRates()` |
| `Aedis.ExchangeRates.Ptax` | 3 | `IExchangeRates` sobre a API pública Olinda do Banco Central; `WithPtax(configuration)` |

```csharp
services.AddAedisExchangeRates().WithPtax(configuration);

var usdBrl = await rates.GetAsync("USD", "BRL", DateOnly.FromDateTime(DateTime.Today), ct);
var centavosBrl = (long)Math.Round(centavosUsd * usdBrl.Sell, MidpointRounding.AwayFromZero);
```

A cotação fica fora de `Aedis.Payments` porque não é assunto de pagamento: conversão de preço, relatório em moeda estrangeira e contabilidade usam a mesma porta, e o provider de pagamento não precisa saber de onde vem a taxa. É um módulo próprio, com o par Abstractions/provider de sempre, para que outra fonte (outro banco central, um provedor comercial) seja troca de pacote.

## PTAX

- **Qual cotação.** `GetAsync(base, "BRL", data)` devolve o último boletim de **Fechamento** (a PTAX oficial, publicada por volta das 13h de Brasília) publicado até a data. Em fim de semana, feriado ou antes da publicação do dia, vale a do último dia útil — por isso uma consulta cobre a janela de `LookbackDays` dias (padrão 10) até a data. Sem fechamento na janela: `ExchangeRateUnavailableException`.
- **Compra e venda.** `ExchangeRate.Buy`/`Sell` são as taxas de compra e venda da PTAX. Para converter um preço em moeda estrangeira que a empresa vai receber, use a de venda.
- **Pares.** Qualquer moeda da PTAX contra o real (`USD`, `EUR`, …). Real como base, ou par sem real, lança `NotSupportedException`.
- **Instante.** `QuotedAt` está no horário de Brasília (UTC−3; sem horário de verão desde 2019). Guarde-o junto do valor convertido, com `Source = "PTAX"`, para auditoria.
- **Configuração** (`ExchangeRates:Ptax`, opcional): `BaseUrl` (HTTPS obrigatório; padrão `https://olinda.bcb.gov.br/olinda/servico/PTAX/versao/v1/odata/`), `LookbackDays` (1–31), `Timeout` (padrão 10 s). A API é pública e não usa credencial.
- **Cache.** O provider consulta a API a cada chamada. Cotações passadas não mudam: se a aplicação converte muitos valores da mesma data, guarde a `ExchangeRate` do dia em vez de consultar de novo.
