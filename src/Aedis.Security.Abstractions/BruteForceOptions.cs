namespace Aedis.Security.Abstractions;

/// <summary>
///     Configura a proteção contra força bruta por credencial, vinculada à seção <c>Security:BruteForce</c>.
///     Ao exceder <see cref="MaxAttempts" /> falhas dentro de <see cref="AttemptWindow" />, a credencial é
///     bloqueada por <see cref="BaseLockout" />; cada bloqueio subsequente (enquanto a memória de
///     escalonamento dura) multiplica a duração por <see cref="EscalationFactor" />, até <see cref="MaxLockout" />.
/// </summary>
public sealed class BruteForceOptions
{
    /// <summary>Nome da seção de configuração que vincula estas opções.</summary>
    public const string SectionName = "Security:BruteForce";

    /// <summary>Número de falhas permitidas na janela antes de bloquear. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Janela de contagem das falhas. Default 15 minutos.</summary>
    public TimeSpan AttemptWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Duração do primeiro bloqueio. Default 1 minuto.</summary>
    public TimeSpan BaseLockout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Fator de multiplicação da duração a cada bloqueio repetido. Default 2,0 (dobra a cada vez).</summary>
    public double EscalationFactor { get; set; } = 2.0;

    /// <summary>Teto da duração de bloqueio, por mais que escale. Default 1 hora.</summary>
    public TimeSpan MaxLockout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Por quanto tempo o nível de escalonamento (número de bloqueios) é lembrado. Reincidências dentro
    ///     desta janela continuam escalando; após ela, o próximo bloqueio volta à duração base. Default 12 horas.
    /// </summary>
    public TimeSpan EscalationWindow { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Prefixo das chaves no cache. Default <c>security:bruteforce:</c>.</summary>
    public string KeyPrefix { get; set; } = "security:bruteforce:";
}
