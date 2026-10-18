namespace Aedis.Payments.Abstractions;

/// <summary>
///     Preço mensal de um plano. <see cref="Catalog" /> usa um preço cadastrado no provedor (ex.: preço em USD na
///     conta global, convertido pelo Adaptive Pricing); <see cref="Custom" /> monta o preço na hora a partir de um
///     produto e de um valor já convertido pela aplicação (ex.: plano em USD cobrado em BRL pela cotação do dia).
/// </summary>
public sealed record PlanPrice
{
    private PlanPrice(string? priceId, string? productId, Money? monthlyAmount) {
        PriceId = priceId;
        ProductId = productId;
        MonthlyAmount = monthlyAmount;
    }

    /// <summary>Id do preço cadastrado no provedor; <c>null</c> em preços <see cref="Custom" />.</summary>
    public string? PriceId { get; }

    /// <summary>Id do produto no provedor; preenchido só em preços <see cref="Custom" />.</summary>
    public string? ProductId { get; }

    /// <summary>Valor mensal; preenchido só em preços <see cref="Custom" />.</summary>
    public Money? MonthlyAmount { get; }

    /// <summary>Preço cadastrado no provedor (ex.: <c>price_…</c>).</summary>
    public static PlanPrice Catalog(string priceId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);
        return new PlanPrice(priceId, null, null);
    }

    /// <summary>Preço mensal montado na hora para o produto <paramref name="productId" />; o valor deve ser positivo.</summary>
    public static PlanPrice Custom(string productId, Money monthlyAmount) {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(monthlyAmount);
        ArgumentOutOfRangeException.ThrowIfZero(monthlyAmount.Amount);
        return new PlanPrice(null, productId, monthlyAmount);
    }
}
