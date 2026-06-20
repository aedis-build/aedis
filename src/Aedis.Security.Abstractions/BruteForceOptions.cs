namespace Aedis.Security.Abstractions;

/// <summary>
///     Configura a proteção contra força bruta por credencial, vinculada à seção <c>Security:BruteForce</c>.
///     Ao exceder <see cref="MaxAttempts" /> falhas dentro de <see cref="AttemptWindow" />, a credencial é
///     bloqueada; a duração do bloqueio (o período de 429) endurece em <strong>três níveis</strong>
///     configuráveis (<see cref="Lockout" />) conforme a reincidência, enquanto a memória de escalonamento
///     (<see cref="EscalationWindow" />) durar.
/// </summary>
public sealed class BruteForceOptions
{
    /// <summary>Nome da seção de configuração que vincula estas opções.</summary>
    public const string SectionName = "Security:BruteForce";

    /// <summary>Número de falhas permitidas na janela antes de bloquear. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Janela de contagem das falhas. Default 15 minutos.</summary>
    public TimeSpan AttemptWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Durações de bloqueio (período de 429) em três níveis, do mais brando ao mais severo.</summary>
    public BruteForceLockoutLevels Lockout { get; set; } = new();

    /// <summary>
    ///     Por quanto tempo o nível de escalonamento (número de bloqueios) é lembrado. Reincidências dentro
    ///     desta janela sobem de nível; após ela, o próximo bloqueio volta ao nível 1. Default 12 horas.
    /// </summary>
    public TimeSpan EscalationWindow { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Prefixo das chaves no cache. Default <c>security:bruteforce:</c>.</summary>
    public string KeyPrefix { get; set; } = "security:bruteforce:";
}

/// <summary>
///     Durações do bloqueio (período de 429) em três níveis crescentes. O 1º bloqueio aplica
///     <see cref="Level1" />; reincidências sobem para <see cref="Level2" /> e depois <see cref="Level3" />,
///     que é o teto e permanece nas reincidências seguintes.
/// </summary>
public sealed class BruteForceLockoutLevels
{
    /// <summary>Duração do bloqueio no 1º nível (primeira reincidência). Default 1 minuto.</summary>
    public TimeSpan Level1 { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Duração do bloqueio no 2º nível. Default 15 minutos.</summary>
    public TimeSpan Level2 { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Duração do bloqueio no 3º nível (teto). Default 1 hora.</summary>
    public TimeSpan Level3 { get; set; } = TimeSpan.FromHours(1);
}
