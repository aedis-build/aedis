using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>
///     Bootstrap e readiness da chave: o hosted service insiste em erro transitório e aborta em permanente
///     (marcando o estado); o health check reflete cada fase e, com <c>ProbeProvider</c>, consulta o provider.
/// </summary>
public sealed class SigningKeyLifecycleTests
{
    private static SigningKeyHealthCheck HealthCheck(SigningKeyState state, SigningOptions options, ISigningKeyProbe? probe = null) {
        var services = new ServiceCollection();
        if (probe is not null) services.AddSingleton(probe);
        return new SigningKeyHealthCheck(state, options, services.BuildServiceProvider(), NullLogger<SigningKeyHealthCheck>.Instance);
    }

    [Fact]
    public async Task Startup_insiste_em_erro_transitorio_e_conclui() {
        using var key = TestSigningKeys.NewP256();
        var handle = TestSigningKeys.Handle(key);
        var state = new SigningKeyState();
        var provider = Substitute.For<ISigningKeyProvider>();
        var attempts = 0;
        provider.EnsureKeyAsync(Arg.Any<CancellationToken>()).Returns(_ => {
            if (++attempts < 3) throw new ServiceTemporarilyUnavailableException("vault", "fora");
            state.SetReady(handle);
            return Task.FromResult(handle);
        });
        var time = new FakeTimeProvider();
        var service = new SigningKeyStartupService(provider, state, new SigningOptions { BootstrapTimeoutSeconds = 60 },
            NullLogger<SigningKeyStartupService>.Instance, time);

        var start = service.StartAsync(CancellationToken.None);
        for (var i = 0; i < 200 && !start.IsCompleted; i++) {
            await Task.Delay(10);
            time.Advance(TimeSpan.FromSeconds(10));
        }

        start.IsCompleted.Should().BeTrue("o bootstrap deveria concluir após as tentativas transitórias");
        await start;
        attempts.Should().Be(3);
        state.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task Startup_aborta_em_erro_permanente_e_marca_o_estado() {
        var state = new SigningKeyState();
        var provider = Substitute.For<ISigningKeyProvider>();
        provider.EnsureKeyAsync(Arg.Any<CancellationToken>())
            .Returns<SigningKeyHandle>(_ => throw new SigningKeyNotFoundException("alias/x", "não existe"));
        var service = new SigningKeyStartupService(provider, state, new SigningOptions(), NullLogger<SigningKeyStartupService>.Instance);

        var act = () => service.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<SigningKeyNotFoundException>();
        state.Fault.Should().BeOfType<SigningKeyNotFoundException>();
    }

    [Fact]
    public async Task Health_segue_as_fases_do_bootstrap() {
        using var key = TestSigningKeys.NewP256();
        var state = new SigningKeyState();
        var options = new SigningOptions();
        var check = HealthCheck(state, options);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy, "chave ainda não resolvida");

        state.SetReady(TestSigningKeys.Handle(key));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        state.MarkDegraded(new SigningKeyInvalidStateException("kid-1", "desabilitada"));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy, "rejeitada em runtime");
    }

    [Fact]
    public async Task Health_com_falha_no_bootstrap_e_unhealthy_e_desligado_e_healthy() {
        var faulted = new SigningKeyState();
        faulted.SetFaulted(new SigningException("falhou"));

        (await HealthCheck(faulted, new SigningOptions()).CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);
        (await HealthCheck(new SigningKeyState(), new SigningOptions { Enabled = false }).CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Health_com_ProbeProvider_consulta_o_provider() {
        using var key = TestSigningKeys.NewP256();
        var state = new SigningKeyState();
        state.SetReady(TestSigningKeys.Handle(key));
        var probe = Substitute.For<ISigningKeyProbe>();
        probe.ProbeAsync(Arg.Any<SigningKeyHandle>(), Arg.Any<CancellationToken>())
            .Returns(new SigningKeyProbeResult(false, "PendingDeletion"));
        var check = HealthCheck(state, new SigningOptions { HealthCheck = { ProbeProvider = true } }, probe);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("PendingDeletion");
    }
}
