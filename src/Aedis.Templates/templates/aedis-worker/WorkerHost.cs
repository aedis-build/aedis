using Aedis.Hosting.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AedisWorker1;

/// <summary>
///     Host do worker. Por herdar de <see cref="AedisWorkerHost" />, já vem com logging estruturado,
///     telemetria OTLP, health probes (endpoint <c>/health</c>) e shutdown gracioso. Para rodar sem
///     servidor HTTP, sobrescreva <c>EnableHealthEndpoint => false</c>.
/// </summary>
public sealed class WorkerHost : AedisWorkerHost
{
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddHostedService<SampleWorker>();
    }
}
