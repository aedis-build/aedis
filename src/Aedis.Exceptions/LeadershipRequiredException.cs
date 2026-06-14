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

    public LeadershipRequiredException(
        string leaderLockKey,
        string processingInstance = "unknown")
        : this(Guid.NewGuid(), leaderLockKey, processingInstance) { }

    public string LeaderLockKey { get; }
}