namespace Aedis.Scheduling.Abstractions;

/// <summary>
///     Contrato de um processador de job agendado. Implementações são registradas como <c>Scoped</c> e
///     podem injetar repositórios e serviços diretamente — <strong>sem nenhuma dependência do scheduler</strong>
///     (Hangfire fica isolado no <c>CronJobExecutor&lt;T&gt;</c>). Trocar de scheduler não toca os processors.
/// </summary>
public interface ICronJobProcessor
{
    /// <summary>Executa o job. O <paramref name="cancellationToken" /> é fornecido pelo scheduler.</summary>
    Task ProcessAsync(CancellationToken cancellationToken);
}
