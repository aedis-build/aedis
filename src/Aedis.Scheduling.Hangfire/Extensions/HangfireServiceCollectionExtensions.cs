using Aedis.Scheduling.Abstractions;
using Aedis.Scheduling.Hangfire;
using global::Hangfire;
using global::Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do scheduler Hangfire do Aedis.
/// </summary>
public static class HangfireServiceCollectionExtensions
{
    /// <summary>
    ///     Configura o Hangfire com storage no PostgreSQL (schema dedicado), o server de processamento e o
    ///     filtro de métricas (coletado pela telemetria do Aedis). Lê as opções da seção <c>Hangfire</c>.
    /// </summary>
    public static IServiceCollection AddAedisHangfire(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<HangfireOptions>()
            .Bind(configuration.GetSection(HangfireOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
                      ?? new HangfireOptions();

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(connection => connection.UseNpgsqlConnection(options.ConnectionString),
                new PostgreSqlStorageOptions { SchemaName = options.SchemaName })
            .UseFilter(new HangfireMetricsFilter()));

        services.AddHangfireServer(server => server.WorkerCount = options.WorkerCount);

        return services;
    }

    /// <summary>
    ///     Registra um processador de job (<see cref="ICronJobProcessor" />) e seu executor (ambos scoped).
    ///     O processor é resolvido por escopo a cada execução, sem tocar o Hangfire.
    /// </summary>
    public static IServiceCollection AddCronJob<TProcessor>(this IServiceCollection services)
        where TProcessor : class, ICronJobProcessor {
        services.TryAddScoped<TProcessor>();
        services.TryAddScoped<CronJobExecutor<TProcessor>>();
        return services;
    }

    /// <summary>
    ///     Agenda (ou atualiza) um job recorrente com a expressão cron informada. Chame após o build do
    ///     provider, no startup. O fuso padrão é UTC.
    /// </summary>
    public static void ScheduleCronJob<TProcessor>(this IServiceProvider serviceProvider, string jobId,
        string cronExpression, TimeZoneInfo? timeZone = null) where TProcessor : class, ICronJobProcessor {
        serviceProvider.GetRequiredService<IRecurringJobManager>()
            .AddOrUpdate<CronJobExecutor<TProcessor>>(jobId, executor => executor.RunAsync(CancellationToken.None),
                cronExpression, new RecurringJobOptions { TimeZone = timeZone ?? TimeZoneInfo.Utc });
    }
}
