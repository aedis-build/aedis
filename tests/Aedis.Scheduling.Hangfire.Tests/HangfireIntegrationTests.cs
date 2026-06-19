using Aedis.Scheduling.Abstractions;
using Aedis.Scheduling.Hangfire;
using FluentAssertions;
using global::Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aedis.Scheduling.Hangfire.Tests;

/// <summary>
///     Pipeline completo do scheduler contra um Hangfire real com storage PostgreSQL (Testcontainers):
///     <c>AddAedisHangfire</c> prepara o storage, o server processa, e um job enfileirado executa o
///     <see cref="ICronJobProcessor" /> via <see cref="CronJobExecutor{T}" />.
/// </summary>
public sealed class HangfireIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Job_enfileirado_executa_o_processor_via_hangfire() {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Hangfire:ConnectionString"] = _container.GetConnectionString(),
            ["Hangfire:SchemaName"] = "hangfire",
            ["Hangfire:WorkerCount"] = "1"
        }).Build();

        var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(signal)
            .AddAedisHangfire(config)
            .AddCronJob<SignalingProcessor>()
            .BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hosted in hostedServices)
            await hosted.StartAsync(CancellationToken.None);

        try {
            provider.GetRequiredService<IBackgroundJobClient>()
                .Enqueue<CronJobExecutor<SignalingProcessor>>(executor => executor.RunAsync(CancellationToken.None));

            var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(40)));
            completed.Should().Be(signal.Task, "o job enfileirado foi processado pelo Hangfire");
        }
        finally {
            foreach (var hosted in hostedServices)
                await hosted.StopAsync(CancellationToken.None);
        }
    }

    private sealed class SignalingProcessor(TaskCompletionSource signal) : ICronJobProcessor
    {
        public Task ProcessAsync(CancellationToken cancellationToken) {
            signal.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
