using Stripe;

namespace Aedis.Payments.Stripe;

/// <summary>
///     Contas Stripe da aplicação, com um <see cref="StripeClient" /> por conta (cada um com a sua chave; nada usa o
///     estado global <c>StripeConfiguration</c>). Testes injetam o <see cref="IHttpClient" /> para falar com um fake.
/// </summary>
internal sealed class StripeAccounts
{
    private readonly Dictionary<string, StripeAccount> _accounts;

    /// <summary>Monta os clientes a partir das opções já validadas.</summary>
    public StripeAccounts(StripeOptions options, IHttpClient? httpClient = null) {
        var apiBase = string.IsNullOrWhiteSpace(options.ApiBase) ? null : options.ApiBase.TrimEnd('/');
        _accounts = options.Accounts.ToDictionary(
            a => a.Key,
            a => new StripeAccount(a.Key, new StripeClient(a.Value.SecretKey, httpClient: httpClient, apiBase: apiBase), a.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Todas as contas, para verificar a assinatura do webhook contra cada segredo.</summary>
    public IEnumerable<StripeAccount> All => _accounts.Values;

    /// <summary>Conta pelo nome.</summary>
    /// <exception cref="ArgumentException">Conta não configurada.</exception>
    public StripeAccount Get(string name) =>
        _accounts.TryGetValue(name, out var account)
            ? account
            : throw new ArgumentException($"Conta Stripe '{name}' não configurada em {StripeOptions.SectionName}:Accounts.", nameof(name));
}
