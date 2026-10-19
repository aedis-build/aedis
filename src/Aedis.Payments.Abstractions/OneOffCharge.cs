namespace Aedis.Payments.Abstractions;

/// <summary>
///     Cobrança avulsa no cartão padrão do cliente, sem ele presente (off-session). O teto por item é regra da
///     aplicação: as linhas já chegam com o valor final.
/// </summary>
/// <param name="Customer">Cliente pagador.</param>
/// <param name="Currency">Moeda ISO 4217 da cobrança (ex.: <c>BRL</c>).</param>
/// <param name="Lines">Linhas da fatura; ao menos uma, todas positivas.</param>
/// <param name="IdempotencyKey">
///     Chave estável da cobrança (ex.: <c>produtividade:{tenant}:{semana}</c>). Repetir a chamada com a mesma chave
///     devolve a mesma fatura em vez de cobrar duas vezes.
/// </param>
/// <param name="Description">Descrição da fatura.</param>
public sealed record OneOffCharge(
    PaymentRef Customer,
    string Currency,
    IReadOnlyList<ChargeLine> Lines,
    string IdempotencyKey,
    string? Description = null);
