using Aedis.Observability.Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using Serilog;

namespace Aedis.Hosting.Worker;

/// <summary>
///     Classe-base de um host headless (worker, consumer de mensageria, processo batch). A aplicação herda,
///     sobrescreve <see cref="ConfigureServices" /> e chama <see cref="RunAsync" /> no <c>Main</c>. Compõe
///     por default logging estruturado (Serilog), telemetria (OTLP), health checks e shutdown gracioso —
///     a mesma base de observabilidade do host de API, sem o pipeline HTTP nem segurança web (que não se
///     aplicam a um processo sem porta exposta).
/// </summary>
/// <remarks>
///     Quando <see cref="EnableHealthEndpoint" /> é <c>true</c> (default), sobe um servidor mínimo apenas
///     para expor <c>/health</c> (probes de liveness/readiness em orquestradores); quando <c>false</c>,
///     roda como host genérico sem servidor HTTP algum.
/// </remarks>
public abstract class AedisWorkerHost
{
    /// <summary>
    ///     Registra os serviços do worker (consumers, jobs, clientes), após o host registrar a infraestrutura
    ///     padrão de observabilidade e shutdown.
    /// </summary>
    protected abstract void ConfigureServices(IConfiguration configuration, IServiceCollection services);

    /// <summary>Adiciona exportadores de métricas extras (ex.: Prometheus) ao pipeline de telemetria.</summary>
    protected virtual void ConfigureMetricsExporters(MeterProviderBuilder builder) { }

    /// <summary>Adiciona health checks específicos do worker além dos padrão (self/uptime/shutdown).</summary>
    protected virtual void AddCustomHealthChecks(IHealthChecksBuilder builder) { }

    /// <summary>Quando <c>true</c> (default), expõe um endpoint HTTP mínimo de health para probes.</summary>
    protected virtual bool EnableHealthEndpoint => true;

    /// <summary>Quando <c>true</c> (default), liga a telemetria OTLP (métricas/traces).</summary>
    protected virtual bool EnableTelemetry => true;

    /// <summary>
    ///     Constrói e executa o worker até o cancelamento. Usa um servidor mínimo quando há endpoint de health
    ///     ou um host genérico sem HTTP caso contrário; em ambos, drena o logger no encerramento.
    /// </summary>
    /// <param name="args">Argumentos de linha de comando recebidos pelo <c>Main</c>.</param>
    /// <param name="cancellationToken">Token que sinaliza o encerramento gracioso.</param>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default) {
        try {
            if (EnableHealthEndpoint)
                await RunWithHealthEndpointAsync(args, cancellationToken);
            else
                await RunHeadlessAsync(args, cancellationToken);
        }
        catch (Exception exception) {
            Log.Fatal(exception, "Falha fatal ao inicializar o worker.");
            throw;
        }
        finally {
            await Log.CloseAndFlushAsync();
        }
    }

    private async Task RunWithHealthEndpointAsync(string[] args, CancellationToken cancellationToken) {
        var app = BuildWebApplication(args);
        await app.RunAsync(cancellationToken);
    }

    /// <summary>
    ///     Constrói o host web mínimo (serviços + endpoint de health) sem executá-lo. Costura de teste:
    ///     <paramref name="configureBuilder" /> roda após a criação do builder, permitindo aos testes ajustar
    ///     ambiente, configuração e servidor (ex.: <c>UseTestServer</c>).
    /// </summary>
    internal WebApplication BuildWebApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder = null) {
        var builder = WebApplication.CreateBuilder(args);
        Log.Logger = AedisSerilog.CreateLogger(builder.Configuration);

        configureBuilder?.Invoke(builder);
        RegisterServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        app.MapAedisHealthChecks();

        return app;
    }

    private async Task RunHeadlessAsync(string[] args, CancellationToken cancellationToken) {
        var builder = Host.CreateApplicationBuilder(args);
        Log.Logger = AedisSerilog.CreateLogger(builder.Configuration);

        RegisterServices(builder.Services, builder.Configuration);

        var host = builder.Build();

        await host.RunAsync(cancellationToken);
    }

    private void RegisterServices(IServiceCollection services, IConfiguration configuration) {
        services.AddAedisSerilog(configuration);

        if (EnableTelemetry)
            services.AddAedisTelemetry(configuration, ConfigureMetricsExporters);

        services.AddAedisDiagnostics();
        AddCustomHealthChecks(services.AddHealthChecks());

        ConfigureServices(configuration, services);
    }
}
