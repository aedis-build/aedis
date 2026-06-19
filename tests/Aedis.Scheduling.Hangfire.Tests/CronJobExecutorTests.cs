using Aedis.Scheduling.Abstractions;
using Aedis.Scheduling.Hangfire;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Scheduling.Hangfire.Tests;

/// <summary>
///     O <see cref="CronJobExecutor{T}" /> resolve o processor em um escopo de DI novo e o executa — o
///     processor não toca o Hangfire (decoupling). Sem banco.
/// </summary>
public sealed class CronJobExecutorTests
{
    [Fact]
    public async Task RunAsync_resolve_o_processor_em_escopo_novo_e_chama_ProcessAsync() {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new ServiceCollection()
            .AddSingleton(signal)
            .AddScoped<RecordingProcessor>()
            .BuildServiceProvider();

        var executor = new CronJobExecutor<RecordingProcessor>(provider.GetRequiredService<IServiceScopeFactory>());

        await executor.RunAsync();

        signal.Task.IsCompletedSuccessfully.Should().BeTrue("o processor foi resolvido e executado");
    }

    private sealed class RecordingProcessor(TaskCompletionSource signal) : ICronJobProcessor
    {
        public Task ProcessAsync(CancellationToken cancellationToken) {
            signal.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
