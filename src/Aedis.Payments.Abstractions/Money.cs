namespace Aedis.Payments.Abstractions;

/// <summary>
///     Valor monetário em <b>unidades mínimas</b> da moeda (centavos para BRL e USD), como os provedores de pagamento
///     esperam; evita arredondamento de <see cref="decimal" /> na fronteira. A moeda é o código ISO 4217 em maiúsculas.
/// </summary>
public sealed record Money
{
    /// <summary>Cria o valor; <paramref name="amount" /> não pode ser negativo e a moeda tem três letras.</summary>
    public Money(long amount, string currency) {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Amount = amount;
        Currency = NormalizeCurrency(currency);
    }

    /// <summary>Valor em unidades mínimas (ex.: <c>10990</c> = R$ 109,90).</summary>
    public long Amount { get; }

    /// <summary>Código ISO 4217 em maiúsculas (ex.: <c>BRL</c>).</summary>
    public string Currency { get; }

    /// <summary>Valida e normaliza um código ISO 4217 (três letras) para maiúsculas.</summary>
    /// <exception cref="ArgumentException">Código vazio ou que não tem três letras.</exception>
    public static string NormalizeCurrency(string currency) {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var code = currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"Moeda '{currency}' não é um código ISO 4217 de três letras.", nameof(currency));
        return code;
    }
}
