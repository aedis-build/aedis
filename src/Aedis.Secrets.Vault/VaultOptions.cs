namespace Aedis.Secrets.Vault;

/// <summary>Método de autenticação no Vault.</summary>
public enum VaultAuthMethod
{
    /// <summary>Token estático (<see cref="VaultOptions.Token" />). Para desenvolvimento e automações fora do cluster.</summary>
    Token,

    /// <summary>
    ///     Kubernetes: troca o token projetado da ServiceAccount do pod (<see cref="VaultOptions.KubernetesTokenPath" />)
    ///     por um token do Vault na role <see cref="VaultOptions.KubernetesRole" />. Sem segredo estático no pod.
    /// </summary>
    Kubernetes,

    /// <summary>AppRole: <see cref="VaultOptions.AppRoleId" /> + <see cref="VaultOptions.AppRoleSecretId" />. Para hosts fora do Kubernetes.</summary>
    AppRole
}

/// <summary>
///     Configuração do provider de segredos do HashiCorp Vault (KV v2). Cada segredo é um path no mount
///     <see cref="MountPoint" />; o valor textual vem do campo <see cref="ValueKey" /> dentro do segredo. A
///     autenticação é por token, Kubernetes ou AppRole (<see cref="Auth" />).
/// </summary>
public sealed class VaultOptions
{
    /// <summary>Nome da seção de configuração (<c>Vault</c>).</summary>
    public const string SectionName = "Vault";

    /// <summary>Endereço do servidor Vault (obrigatório), ex.: <c>http://localhost:8200</c>.</summary>
    public string? Address { get; set; }

    /// <summary>Método de autenticação. Padrão <see cref="VaultAuthMethod.Token" />.</summary>
    public VaultAuthMethod Auth { get; set; } = VaultAuthMethod.Token;

    /// <summary>Token de autenticação (obrigatório com <see cref="VaultAuthMethod.Token" />).</summary>
    public string? Token { get; set; }

    /// <summary>Role do Vault (obrigatória com <see cref="VaultAuthMethod.Kubernetes" />).</summary>
    public string? KubernetesRole { get; set; }

    /// <summary>Mount path do auth Kubernetes. Padrão <c>kubernetes</c>.</summary>
    public string KubernetesMountPath { get; set; } = "kubernetes";

    /// <summary>Arquivo com o token projetado da ServiceAccount. Padrão <c>/var/run/secrets/kubernetes.io/serviceaccount/token</c>.</summary>
    public string KubernetesTokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    /// <summary>RoleId do AppRole (obrigatório com <see cref="VaultAuthMethod.AppRole" />).</summary>
    public string? AppRoleId { get; set; }

    /// <summary>SecretId do AppRole (obrigatório com <see cref="VaultAuthMethod.AppRole" />).</summary>
    public string? AppRoleSecretId { get; set; }

    /// <summary>Mount path do auth AppRole. Padrão <c>approle</c>.</summary>
    public string AppRoleMountPath { get; set; } = "approle";

    /// <summary>Mount point do engine KV v2. Padrão <c>secret</c>.</summary>
    public string MountPoint { get; set; } = "secret";

    /// <summary>Campo, dentro do segredo, que guarda o valor textual. Padrão <c>value</c>.</summary>
    public string ValueKey { get; set; } = "value";

    /// <summary>Mensagem do que falta para o método escolhido, ou <c>null</c> quando as opções estão completas.</summary>
    public string? Validate() {
        if (string.IsNullOrWhiteSpace(Address)) return "Vault:Address é obrigatório.";
        return Auth switch {
            VaultAuthMethod.Token when string.IsNullOrWhiteSpace(Token) => "Vault:Token é obrigatório com Vault:Auth=Token.",
            VaultAuthMethod.Kubernetes when string.IsNullOrWhiteSpace(KubernetesRole) => "Vault:KubernetesRole é obrigatório com Vault:Auth=Kubernetes.",
            VaultAuthMethod.AppRole when string.IsNullOrWhiteSpace(AppRoleId) || string.IsNullOrWhiteSpace(AppRoleSecretId) =>
                "Vault:AppRoleId e Vault:AppRoleSecretId são obrigatórios com Vault:Auth=AppRole.",
            _ => null
        };
    }
}
