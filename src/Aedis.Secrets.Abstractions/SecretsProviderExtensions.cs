namespace Aedis.Secrets.Abstractions;

/// <summary>Conveniências sobre <see cref="ISecretsProvider" /> para leitura obrigatória de segredos.</summary>
public static class SecretsProviderExtensions
{
    /// <summary>
    ///     Lê o segredo <paramref name="name" /> e lança <see cref="SecretNotFoundException" /> se ele não
    ///     existir — para segredos cuja ausência deve falhar o startup/operação em vez de seguir com <c>null</c>.
    /// </summary>
    public static async Task<string> GetRequiredSecretAsync(this ISecretsProvider provider, string name,
        CancellationToken cancellationToken = default) =>
        await provider.GetSecretAsync(name, cancellationToken)
        ?? throw new SecretNotFoundException(name);
}
