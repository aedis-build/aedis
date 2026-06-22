using Aedis.App1.Worker.Messaging;
using Aedis.Hosting.Worker;
using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.App1.Worker;

/// <summary>
///     Host do worker adicionado a uma solução existente. Por herdar de <see cref="AedisWorkerHost" />, já vem
///     com logging estruturado (com ofuscação de PII/segredos), telemetria OTLP, health probe e shutdown
///     gracioso. Compõe o broker, registra o handler e liga o consumidor. Para persistir, reuse as camadas da
///     sua solução (ver o handler e o csproj).
/// </summary>
public sealed class WorkerHost : AedisWorkerHost {
    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddAedisRabbitMq(configuration);
        services.AddScoped<IMessageHandler<NotificationRequestedEvent>, NotificationRequestedEventHandler>();
        services.AddHostedService<NotificationConsumerService>();

        // Para reusar suas camadas (persistência): adicione as ProjectReference no csproj e chame
        // services.AddInfrastructure(configuration);
    }
}
