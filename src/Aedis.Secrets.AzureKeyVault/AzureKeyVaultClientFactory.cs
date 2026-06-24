using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Aedis.Secrets.AzureKeyVault;

/// <summary>
///     Constrói o <see cref="SecretClient" /> a partir das opções, autenticando com
///     <c>DefaultAzureCredential</c> (cadeia padrão do Azure).
/// </summary>
internal static class AzureKeyVaultClientFactory
{
    public static SecretClient Build(AzureKeyVaultOptions options) {
        if (string.IsNullOrWhiteSpace(options.VaultUri))
            throw new InvalidOperationException("AzureKeyVault:VaultUri é obrigatório.");

        return new SecretClient(new Uri(options.VaultUri), new DefaultAzureCredential());
    }
}
