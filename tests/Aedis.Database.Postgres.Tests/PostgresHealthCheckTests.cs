using Aedis.Database.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     Health check do PostgreSQL sem banco: a sondagem tem teto (<c>HealthCheckTimeout</c>) e, estourado, o
///     resultado é <c>Degraded</c>; o resultado é cacheado por TTL, então probes repetidas não tocam o banco.
/// </summary>
public sealed class PostgresHealthCheckTests
{
    [Fact]
    public async Task Sondagem_que_excede_o_timeout_responde_Degraded() {
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).Returns(async call => {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            return Substitute.For<IUnitOfWork>();
        });
        var options = Options.Create(new DatabaseOptions { HealthCheckTimeout = TimeSpan.FromMilliseconds(50) });
        var check = new PostgresHealthCheck(factory, options, NullLogger<PostgresHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Resultado_e_cacheado_por_TTL() {
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fora"));
        var options = Options.Create(new DatabaseOptions { HealthCheckCacheTtl = TimeSpan.FromMinutes(1) });
        var check = new PostgresHealthCheck(factory, options, NullLogger<PostgresHealthCheck>.Instance);

        var first = await check.CheckHealthAsync(new HealthCheckContext());
        var second = await check.CheckHealthAsync(new HealthCheckContext());

        first.Status.Should().Be(HealthStatus.Unhealthy);
        second.Status.Should().Be(HealthStatus.Unhealthy);
        await factory.Received(1).CreateReadSessionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelamento_do_chamador_propaga() {
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).Returns(async call => {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            return Substitute.For<IUnitOfWork>();
        });
        var check = new PostgresHealthCheck(factory, Options.Create(new DatabaseOptions()), NullLogger<PostgresHealthCheck>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Theory]
    [InlineData(1, HealthStatus.Healthy)]
    [InlineData(0, HealthStatus.Unhealthy)]
    public async Task Sondagem_responde_pelo_valor_devolvido(int value, HealthStatus expected) {
        var session = Substitute.For<IUnitOfWork>();
        session.QuerySingleOrDefaultAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(value);
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        var check = new PostgresHealthCheck(factory, Options.Create(new DatabaseOptions()), NullLogger<PostgresHealthCheck>.Instance);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(expected);
    }
}
