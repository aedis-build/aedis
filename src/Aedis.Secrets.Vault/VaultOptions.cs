namespace Aedis.Secrets.Vault;

/// <summary>
///     Configuração do provider de segredos do HashiCorp Vault (KV v2, autenticação por token). Cada
///     segredo é um path no mount <see cref="MountPoint" />; o valor textual vem do campo
///     <see cref="ValueKey" /> dentro do segredo.
/// </summary>
public sealed class VaultOptions
{
    /// <summary>Nome da seção de configuração (<c>Vault</c>).</summary>
    public const string SectionName = "Vault";

    /// <summary>Endereço do servidor Vault (obrigatório), ex.: <c>http://localhost:8200</c>.</summary>
    public string? Address { get; set; }

    /// <summary>Token de autenticação (obrigatório).</summary>
    public string? Token { get; set; }

    /// <summary>Mount point do engine KV v2. Padrão <c>secret</c>.</summary>
    public string MountPoint { get; set; } = "secret";

    /// <summary>Campo, dentro do segredo, que guarda o valor textual. Padrão <c>value</c>.</summary>
    public string ValueKey { get; set; } = "value";
}
