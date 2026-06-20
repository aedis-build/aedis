using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AedisWorker1;

/// <summary>Worker de exemplo: registra um batimento periódico. Substitua pela sua lógica (consumer, batch, etc.).</summary>
public sealed class SampleWorker(ILogger<SampleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            logger.LogInformation("Worker em execução: {Timestamp}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
