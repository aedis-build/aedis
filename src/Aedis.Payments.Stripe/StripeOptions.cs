namespace Aedis.Payments.Stripe;

/// <summary>
///     Opções do provider Stripe (seção <c>Payments:Stripe</c>). Cada entrada de <see cref="Accounts" /> é uma conta
///     Stripe (ex.: <c>br</c> em BRL e <c>global</c> com Adaptive Pricing, na mesma Organization); o cliente nasce numa
///     conta e todas as operações dele usam a mesma. As chaves chegam por um provider de segredos (<c>Aedis.Secrets.*</c>)
///     ou variável de ambiente, nunca versionadas no <c>appsettings</c>.
/// </summary>
public sealed class StripeOptions
{
    /// <summary>Nome da seção de configuração (<c>Payments:Stripe</c>).</summary>
    public const string SectionName = "Payments:Stripe";

    /// <summary>Contas por nome; o nome é o <see cref="Abstractions.PaymentRef.Account" /> que a aplicação guarda.</summary>
    public Dictionary<string, StripeAccountOptions> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Aceita chaves live (<c>sk_live_…</c>). Desligado por padrão: fora de produção só sobe com chaves de teste,
    ///     então uma chave real copiada para outro ambiente falha na subida em vez de cobrar alguém.
    /// </summary>
    public bool AllowLiveMode { get; set; }

    /// <summary>URL base da API (ex.: <c>http://localhost:12111</c> do stripe-mock em testes); vazio usa a da Stripe.</summary>
    public string? ApiBase { get; set; }
}
