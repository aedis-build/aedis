namespace Aedis.Security.Abstractions;

/// <summary>
///     Configura a revogação de tokens, vinculada à seção <c>Security:TokenDenylist</c>. A
///     <see cref="DefaultRevocationLifetime" /> é o TTL aplicado quando o operador revoga sem informar a
///     validade exata — deve cobrir a maior vida possível de um token em circulação, para a revogação valer
///     enquanto qualquer token afetado ainda puder ser usado.
/// </summary>
public sealed class TokenDenylistOptions
{
    /// <summary>Nome da seção de configuração que vincula estas opções.</summary>
    public const string SectionName = "Security:TokenDenylist";

    /// <summary>Vida padrão de uma revogação quando não especificada. Default 24 horas.</summary>
    public TimeSpan DefaultRevocationLifetime { get; set; } = TimeSpan.FromHours(24);
}
