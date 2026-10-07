using Aedis.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Diagnostics.Tests;

/// <summary>
///     Garante que o <c>ShutdownTimeout</c> das opções de desligamento gracioso chega ao
///     <see cref="HostOptions" /> do host e que a validação impede um timeout menor que a drenagem — cenário
///     em que o host abortaria o desligamento no meio.
/// </summary>
public sealed class GracefulShutdownOptionsWiringTests
{
    [Fact]
    public void ShutdownTimeout_e_propagado_para_HostOptions() {
        var services = new ServiceCollection();
        services.AddAedisDiagnostics(options => options.ShutdownTimeout = TimeSpan.FromSeconds(45));

        using var provider = services.BuildServiceProvider();
        var hostOptions = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        hostOptions.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Padrao_e_30_segundos_e_cobre_a_drenagem_padrao() {
        var services = new ServiceCollection();
        services.AddAedisDiagnostics();

        using var provider = services.BuildServiceProvider();
        var graceful = provider.GetRequiredService<IOptions<GracefulShutdownOptions>>().Value;

        graceful.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(30));
        graceful.ShutdownTimeout.Should().BeGreaterThanOrEqualTo(graceful.DrainDelay);
    }

    [Fact]
    public void ShutdownTimeout_menor_que_DrainDelay_falha_na_validacao() {
        var services = new ServiceCollection();
        services.AddAedisDiagnostics(options => {
            options.DrainDelay = TimeSpan.FromSeconds(10);
            options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<GracefulShutdownOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ShutdownTimeout*DrainDelay*");
    }
}
