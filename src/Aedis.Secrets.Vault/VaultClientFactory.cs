using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace Aedis.Secrets.Vault;

/// <summary>Constrói o <see cref="IVaultClient" /> a partir das opções (autenticação por token).</summary>
internal static class VaultClientFactory
{
    public static IVaultClient Build(VaultOptions options) {
        if (string.IsNullOrWhiteSpace(options.Address))
            throw new InvalidOperationException("Vault:Address é obrigatório.");
        if (string.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException("Vault:Token é obrigatório.");

        var settings = new VaultClientSettings(options.Address, new TokenAuthMethodInfo(options.Token));
        return new VaultClient(settings);
    }
}
