namespace Aedis.Payments.Abstractions;

/// <summary>
///     Referência a um objeto no provedor (cliente, assinatura, fatura) junto da conta onde ele vive. Ids do provedor
///     só valem dentro da conta que os criou, então a aplicação guarda os dois (ex.: <c>br</c> + <c>cus_123</c>).
/// </summary>
public sealed record PaymentRef
{
    /// <summary>Cria a referência; conta e id são obrigatórios.</summary>
    public PaymentRef(string account, string id) {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Account = account;
        Id = id;
    }

    /// <summary>Nome da conta configurada no provider (ex.: <c>br</c>, <c>global</c>).</summary>
    public string Account { get; }

    /// <summary>Id do objeto no provedor (ex.: <c>cus_…</c>, <c>sub_…</c>, <c>in_…</c>).</summary>
    public string Id { get; }
}
