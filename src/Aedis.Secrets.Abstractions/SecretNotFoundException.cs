namespace Aedis.Secrets.Abstractions;

/// <summary>
///     Lançada quando um segredo obrigatório não é encontrado no cofre. Sinaliza erro de configuração
///     (segredo ausente/nome errado), não falha transitória — não deve ser tratada com retry.
/// </summary>
public sealed class SecretNotFoundException : Exception
{
    /// <summary>Cria a exceção para o segredo de nome <paramref name="secretName" /> não encontrado.</summary>
    public SecretNotFoundException(string secretName)
        : base($"Segredo obrigatório não encontrado no cofre: '{secretName}'.") =>
        SecretName = secretName;

    /// <summary>Nome do segredo que não foi encontrado.</summary>
    public string SecretName { get; }
}
