namespace Aedis.AI.Abstractions;

/// <summary>
///     Motivos de <see cref="SessionStopped.Reason" />. Os valores seguem os do Managed Agents, mais
///     <see cref="Terminated" /> e <see cref="Error" />; provedores mapeiam os seus para estes.
/// </summary>
public static class StopReasons
{
    /// <summary>O agente terminou o turno e espera a próxima mensagem.</summary>
    public const string EndTurn = "end_turn";

    /// <summary>A sessão espera respostas de ferramentas (<see cref="ToolResult" />).</summary>
    public const string RequiresAction = "requires_action";

    /// <summary>A sessão atingiu o teto de gasto (<see cref="NewSession.BudgetCents" />).</summary>
    public const string BudgetReached = "budget_reached";

    /// <summary>O provedor esgotou as tentativas internas.</summary>
    public const string RetriesExhausted = "retries_exhausted";

    /// <summary>A sessão foi encerrada e não aceita mais envios.</summary>
    public const string Terminated = "terminated";

    /// <summary>Erro na sessão ou no acompanhamento; o detalhe vem em <see cref="SessionStopped.Detail" />.</summary>
    public const string Error = "error";
}
