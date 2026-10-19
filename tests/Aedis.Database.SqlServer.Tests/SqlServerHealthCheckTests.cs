using Aedis.Database.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     Health check do SQL Server sem banco: a sondagem tem teto (<c>HealthCheckTimeout</c>) e, estourado, o
///     resultado é <c>Degraded</c>; o resultado é cacheado por TTL, então probes repetidas não tocam o banco.
/// </summary>
public sealed class SqlServerHealthCheckTests
{
    [Fact]
    public async Task Sondagem_que_excede_o_timeout_responde_Degraded() {
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).Returns(async call => {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            return Substitute.For<IUnitOfWork>();
        });
        var options = Options.Create(new DatabaseOptions { HealthCheckTimeout = TimeSpan.FromMilliseconds(50) });
        var check = new SqlServerHealthCheck(factory, options, NullLogger<SqlServerHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Resultado_e_cacheado_por_TTL() {
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fora"));
        var options = Options.Create(new DatabaseOptions { HealthCheckCacheTtl = TimeSpan.FromMinutes(1) });
        var check = new SqlServerHealthCheck(factory, options, NullLogger<SqlServerHealthCheck>.Instance);

        await check.CheckHealthAsync(new HealthCheckContext());
        var second = await check.CheckHealthAsync(new HealthCheckContext());

        second.Status.Should().Be(HealthStatus.Unhealthy);
        await factory.Received(1).CreateReadSessionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, HealthStatus.Healthy)]
    [InlineData(0, HealthStatus.Unhealthy)]
    public async Task Sondagem_responde_pelo_valor_devolvido(int value, HealthStatus expected) {
        var session = Substitute.For<IUnitOfWork>();
        session.QuerySingleOrDefaultAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(value);
        var factory = Substitute.For<IUnitOfWorkFactory>();
        factory.CreateReadSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        var check = new SqlServerHealthCheck(factory, Options.Create(new DatabaseOptions()), NullLogger<SqlServerHealthCheck>.Instance);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(expected);
    }
}
