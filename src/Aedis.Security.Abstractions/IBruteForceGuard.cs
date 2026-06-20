namespace Aedis.Security.Abstractions;

/// <summary>
///     Estado de força bruta de uma credencial: se está bloqueada, quantas falhas acumulou na janela e, se
///     bloqueada, em quanto tempo o bloqueio expira.
/// </summary>
/// <param name="IsBlocked">Indica se novas tentativas estão bloqueadas no momento.</param>
/// <param name="FailedAttempts">Falhas acumuladas na janela atual.</param>
/// <param name="RetryAfter">Tempo restante até liberar (para o cabeçalho <c>Retry-After</c>), ou <c>null</c> se não bloqueado.</param>
public sealed record BruteForceStatus(bool IsBlocked, int FailedAttempts, TimeSpan? RetryAfter);

/// <summary>
///     Proteção contra força bruta <strong>chaveada pela credencial alvo</strong> (ex.: usuário/conta), não
///     pelo IP de origem — por isso é imune a ataques que rotacionam IPs. Conta as falhas de autenticação por
///     credencial em uma janela e, ao exceder o limite, bloqueia novas tentativas por um período que
///     <strong>escala a cada bloqueio repetido</strong>. Implementada sobre um armazenamento distribuído
///     (<c>ICache</c>), a contagem vale para toda a frota de instâncias.
/// </summary>
/// <remarks>
///     Uso típico no fluxo de login: <c>CheckAsync</c> antes de tentar autenticar (se bloqueado, responder
///     429 com <c>Retry-After</c>); <c>RegisterFailureAsync</c> a cada credencial inválida;
///     <c>ResetAsync</c> ao autenticar com sucesso. Cobre OWASP A07 e mitiga MITRE T1110 (incl. T1110.003
///     password spraying com rotação de IP).
/// </remarks>
public interface IBruteForceGuard
{
    /// <summary>Consulta o estado atual da <paramref name="key" /> (credencial) sem registrar tentativa.</summary>
    Task<BruteForceStatus> CheckAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Registra uma falha de autenticação para a <paramref name="key" />. Ao atingir o limite, ativa o
    ///     bloqueio com duração escalonada e devolve o estado já bloqueado. Se já estiver bloqueada, apenas
    ///     devolve o estado corrente (não acumula durante o bloqueio).
    /// </summary>
    Task<BruteForceStatus> RegisterFailureAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Zera o estado da <paramref name="key" /> (falhas, bloqueio e escalonamento) após sucesso de autenticação.</summary>
    Task ResetAsync(string key, CancellationToken cancellationToken = default);
}
