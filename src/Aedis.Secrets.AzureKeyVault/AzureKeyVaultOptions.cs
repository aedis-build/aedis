namespace Aedis.Secrets.AzureKeyVault;

/// <summary>
///     Configuração do provider de segredos do Azure Key Vault. A autenticação usa
///     <c>DefaultAzureCredential</c> (a cadeia padrão do Azure: managed identity, variáveis de ambiente,
///     Azure CLI) — não há chaves aqui, só a URI do cofre.
/// </summary>
public sealed class AzureKeyVaultOptions
{
    /// <summary>Nome da seção de configuração (<c>AzureKeyVault</c>).</summary>
    public const string SectionName = "AzureKeyVault";

    /// <summary>URI do cofre (obrigatória), ex.: <c>https://meu-cofre.vault.azure.net/</c>.</summary>
    public string? VaultUri { get; set; }

    /// <summary>
    ///     Segredo usado como canário pelo health check (o valor lido é descartado, nunca logado). Quando
    ///     definido, ele precisa existir — escolha um segredo sem conteúdo sensível. Quando vazio, o probe lê um
    ///     nome sentinela e trata HTTP 404 como saudável — prova conectividade e autenticação sem exigir a
    ///     permissão de listar segredos.
    /// </summary>
    public string? HealthCheckSecretName { get; set; }
}
