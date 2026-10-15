using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.AuthMethods.Token;

namespace Aedis.Secrets.Vault;

/// <summary>Constrói o <see cref="IVaultClient" /> a partir das opções, com o método de autenticação escolhido.</summary>
internal static class VaultClientFactory
{
    public static IVaultClient Build(VaultOptions options) {
        if (options.Validate() is { } error)
            throw new InvalidOperationException(error);

        return new VaultClient(new VaultClientSettings(options.Address, AuthMethod(options)));
    }

    internal static IAuthMethodInfo AuthMethod(VaultOptions options) => options.Auth switch {
        VaultAuthMethod.Kubernetes => new KubernetesAuthMethodInfo(options.KubernetesMountPath, options.KubernetesRole,
            File.ReadAllText(options.KubernetesTokenPath).Trim()),
        VaultAuthMethod.AppRole => new AppRoleAuthMethodInfo(options.AppRoleMountPath, options.AppRoleId, options.AppRoleSecretId),
        _ => new TokenAuthMethodInfo(options.Token)
    };
}
