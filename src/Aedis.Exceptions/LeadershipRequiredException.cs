namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando a instância não consegue adquirir liderança para processar uma operação.
///     Comportamento: Outra instância está executando, esta deve aguardar próxima tentativa.
///     Herda de MessageBeingProcessedException para sinalizar concorrência esperada em ambiente multi-instância.
///     Comportamento no RabbitMQ: NACK requeue=true (mensagem volta para a fila).
///     Comportamento em CronJob: Skip execution (aguarda próximo trigger).
/// </summary>
public class LeadershipRequiredException : MessageBeingProcessedException
{
    /// <summary>Cria a exceção para uma mensagem específica, identificando o lock de liderança e a instância que o detém.</summary>
    public LeadershipRequiredException(
        Guid messageId,
        string leaderLockKey,
        string processingInstance = "unknown")
        : base(
            messageId,
            leaderLockKey,
            processingInstance) {
        LeaderLockKey = leaderLockKey;
    }

    /// <summary>Cria a exceção sem mensagem associada (gera um <see cref="Guid" /> novo), útil para jobs/cron sem mensagem de origem.</summary>
    public LeadershipRequiredException(
        string leaderLockKey,
        string processingInstance = "unknown")
        : this(Guid.NewGuid(), leaderLockKey, processingInstance) { }

    /// <summary>Chave do lock de liderança que esta instância não conseguiu adquirir.</summary>
    public string LeaderLockKey { get; }
}