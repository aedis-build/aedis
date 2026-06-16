using Aedis.Database.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Database.Postgres.Tests;

/// <summary>
///     <c>AddAedisPostgres()</c> sem banco: vínculo de options, registro dos repositórios (open generic) e
///     do health check <c>postgres</c> com a tag <c>ready</c>.
/// </summary>
public sealed class PostgresRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = "Host=localhost;Database=x;Username=u;Password=p",
            ["Database:BulkInsertChunkSize"] = "5000"
        }).Build();

    [Fact]
    public void Vincula_options_e_registra_repositorios() {
        var services = new ServiceCollection().AddLogging().AddAedisPostgres(Config());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.BulkInsertChunkSize.Should().Be(5000);
        services.Should().Contain(d => d.ServiceType == typeof(IRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IReadRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IWriteRepository<,>));
    }

    [Fact]
    public void Registra_health_check_postgres_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisPostgres(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "postgres").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
