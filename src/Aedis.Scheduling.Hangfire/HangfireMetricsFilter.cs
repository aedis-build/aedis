using System.Diagnostics.Metrics;
using Hangfire.Common;
using Hangfire.States;

namespace Aedis.Scheduling.Hangfire;

/// <summary>
///     Filtro global do Hangfire que emite métricas via <see cref="System.Diagnostics.Metrics" /> — contadas
///     por sucesso/falha e a duração de cada job. O Meter é coletado pela telemetria do Aedis
///     (<c>AddMeter("*")</c>) e exportado por OTLP, sem configuração adicional.
/// </summary>
public sealed class HangfireMetricsFilter : JobFilterAttribute, IElectStateFilter
{
    private static readonly Meter Meter = new("Aedis.Scheduling.Hangfire");

    private static readonly Counter<long> SuccessCounter =
        Meter.CreateCounter<long>("hangfire.job.success_total", description: "Jobs Hangfire concluídos com sucesso");

    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>("hangfire.job.failure_total", description: "Jobs Hangfire que falharam");

    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>("hangfire.job.duration_ms", "ms", "Duração da execução do job Hangfire");

    /// <summary>
    ///     Chamado pelo Hangfire na eleição do estado final do job. Incrementa o contador de sucesso e registra
    ///     a latência quando o estado é <c>Succeeded</c>, ou incrementa o contador de falha (com o tipo da
    ///     exceção) quando é <c>Failed</c>. Cada métrica é rotulada com o nome do job.
    /// </summary>
    public void OnStateElection(ElectStateContext context) {
        var jobName = context.BackgroundJob.Job.Type.Name;

        switch (context.CandidateState) {
            case SucceededState succeeded:
                SuccessCounter.Add(1, new KeyValuePair<string, object?>("job_name", jobName));
                DurationHistogram.Record(succeeded.Latency, new KeyValuePair<string, object?>("job_name", jobName));
                break;

            case FailedState failed:
                FailureCounter.Add(1,
                    new KeyValuePair<string, object?>("job_name", jobName),
                    new KeyValuePair<string, object?>("exception_type", failed.Exception?.GetType().Name ?? "Unknown"));
                break;
        }
    }
}
