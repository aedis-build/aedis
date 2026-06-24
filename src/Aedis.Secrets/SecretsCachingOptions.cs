namespace Aedis.Secrets;

/// <summary>
///     Opções do cache de segredos em memória (<see cref="CachingSecretsProvider" />). Evita ir ao cofre a
///     cada leitura — relevante porque cofres cobram por chamada e impõem rate limit. O TTL define a janela
///     máxima até uma rotação no cofre passar a ser observada.
/// </summary>
public sealed class SecretsCachingOptions
{
    /// <summary>Nome da seção de configuração (<c>Secrets</c>).</summary>
    public const string SectionName = "Secrets";

    /// <summary>Liga/desliga o cache em memória dos segredos. Padrão ligado.</summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>Tempo de vida de cada segredo no cache antes de reler do cofre. Padrão 5 minutos.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
