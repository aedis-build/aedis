using Aedis.Hosting.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Diagnostics;

/// <summary>
///     Orquestra o desligamento gracioso host-agnóstico ao receber o sinal de parada
///     (<see cref="IHostApplicationLifetime.ApplicationStopping" />). Sequência:
///     <list type="number">
///         <item>marca o <see cref="ShutdownHealthCheck" /> como em desligamento (<c>/health/ready</c> → Unhealthy);</item>
///         <item>aguarda <see cref="GracefulShutdownOptions.DrainDelay" /> para a propagação da readiness;</item>
///         <item>executa os <see cref="IShutdownCleanup" /> registrados;</item>
///         <item>descarta todos os recursos do <see cref="IDisposableRegistry" /> — liberando os locks de liderança.</item>
///     </list>
///     A drenagem de requisições HTTP em andamento é responsabilidade do host ASP.NET.
/// </summary>
public sealed class GracefulShutdownHostedService : IHostedService
{
    private readonly IEnumerable<IShutdownCleanup> _cleanupHandlers;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownHostedService> _logger;
    private readonly GracefulShutdownOptions _options;
    private readonly IDisposableRegistry _registry;
    private readonly ShutdownHealthCheck _shutdownHealthCheck;

    public GracefulShutdownHostedService(
        IHostApplicationLifetime lifetime,
        ShutdownHealthCheck shutdownHealthCheck,
        IDisposableRegistry registry,
        IEnumerable<IShutdownCleanup> cleanupHandlers,
        IOptions<GracefulShutdownOptions> options,
        ILogger<GracefulShutdownHostedService> logger) {
        _lifetime = lifetime;
        _shutdownHealthCheck = shutdownHealthCheck;
        _registry = registry;
        _cleanupHandlers = cleanupHandlers;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        _lifetime.ApplicationStopping.Register(OnStopping);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnStopping() {
        _logger.LogDebug("Desligamento gracioso iniciado (sinal de parada recebido).");

        _shutdownHealthCheck.MarkAsShuttingDown();
        _logger.LogDebug("/health/ready marcado como Unhealthy para remover a instância do roteamento.");

        if (_options.DrainDelay > TimeSpan.Zero) {
            _logger.LogDebug("Aguardando {DrainDelaySeconds}s para a readiness propagar...",
                _options.DrainDelay.TotalSeconds);
            Thread.Sleep(_options.DrainDelay);
        }

        ExecuteCleanupHandlers();
        DisposeAllResources();

        _logger.LogDebug("Sequência de desligamento gracioso concluída.");
    }

    private void ExecuteCleanupHandlers() {
        var handlers = _cleanupHandlers as IReadOnlyCollection<IShutdownCleanup> ?? _cleanupHandlers.ToArray();
        if (handlers.Count == 0) {
            _logger.LogDebug("Nenhum handler de limpeza registrado.");
            return;
        }

        _logger.LogDebug("Executando {Count} handlers de limpeza...", handlers.Count);

        foreach (var cleanup in handlers)
            try {
                cleanup.CleanupAsync(CancellationToken.None).GetAwaiter().GetResult();
                _logger.LogDebug("Handler de limpeza {HandlerType} concluído.", cleanup.GetType().Name);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Erro no handler de limpeza {HandlerType}", cleanup.GetType().Name);
            }
    }

    private void DisposeAllResources() {
        try {
            _logger.LogDebug("Descartando todos os recursos registrados...");
            _registry.DisposeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogDebug("Todos os recursos registrados foram descartados.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao descartar os recursos registrados.");
        }
    }
}
