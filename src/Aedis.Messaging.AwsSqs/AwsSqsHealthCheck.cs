using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Health check de <em>readiness</em> do AWS SQS/SNS — verifica se os clientes inicializam
///     (região/credenciais). Evita <c>ListQueues</c>/<c>ListTopics</c> que exigem permissão global.
/// </summary>
public sealed class AwsSqsHealthCheck(
    IAwsPubSubFactory factory,
    IOptions<AwsSqsOptions> options,
    ILogger<AwsSqsHealthCheck> logger)
    : IHealthCheck
{
    private readonly AwsSqsOptions _options = options.Value;

    /// <summary>Reporta <c>Healthy</c> se os clientes SQS (e SNS, quando aplicável) inicializam; senão <c>Unhealthy</c>.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        try {
            await factory.GetSqsClientAsync(cancellationToken);
            if (_options.UseTopics)
                await factory.GetSnsClientAsync(cancellationToken);

            return HealthCheckResult.Healthy("Conexão AWS SQS/SNS saudável.");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Health check do AWS SQS/SNS falhou.");
            return HealthCheckResult.Unhealthy("Conexão AWS SQS/SNS indisponível.", ex);
        }
    }
}
