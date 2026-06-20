namespace Aedis.Security.Abstractions;

/// <summary>
///     Liga a proteção contra força bruta ao <strong>token/usuário autenticado atual</strong>: aplica o
///     <see cref="IBruteForceGuard" /> chaveado pelo <c>jti</c> (token) e pelo <c>sub</c> (conta) do principal
///     corrente e, quando o token cruza para bloqueio, revoga-o na <see cref="ITokenDenylist" /> pela vida
///     restante. Fecha o cenário de token vazado: tentativas autenticadas abusivas escalam o bloqueio e, no
///     limite, o token é revogado de fato (passando a ser recusado na validação do JWT) — tudo independente
///     do IP de origem.
/// </summary>
public interface ITokenAbuseGuard
{
    /// <summary>Verifica se o token/usuário atual está revogado ou bloqueado, sem registrar tentativa.</summary>
    Task<BruteForceStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Registra uma falha de operação autenticada para o token e a conta atuais. Ao bloquear o token,
    ///     revoga-o na denylist. Devolve o estado (priorizando o bloqueio do token).
    /// </summary>
    Task<BruteForceStatus> RegisterFailureAsync(CancellationToken cancellationToken = default);

    /// <summary>Zera o estado de força bruta do token e da conta atuais (ex.: após operação legítima bem-sucedida).</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Revoga imediatamente o token atual (ex.: logout forçado / resposta a comprometimento), pela vida restante.</summary>
    Task RevokeCurrentTokenAsync(CancellationToken cancellationToken = default);
}
