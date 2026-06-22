using Aedis.App1.Worker.Messaging;
using Aedis.Hosting.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.App1.Worker;

/// <summary>
///     Host do worker. Por herdar de <see cref="AedisWorkerHost" />, já vem com logging estruturado (com
///     ofuscação de PII/segredos), telemetria OTLP, health probe (<c>/health</c>) e shutdown gracioso. Aqui o
///     host apenas compõe as camadas (broker, aplicação, infraestrutura) e liga o consumidor de mensagens.
/// </summary>
public sealed class WorkerHost : AedisWorkerHost {
    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddAedisRabbitMq(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddHostedService<NotificationConsumerService>();
    }
}
