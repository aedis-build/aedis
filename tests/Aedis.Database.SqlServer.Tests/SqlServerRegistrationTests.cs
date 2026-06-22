using Aedis.Database.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     <c>AddAedisSqlServer()</c> sem banco: vínculo de options, registro dos repositórios (open generic) e
///     do health check <c>sqlserver</c> com a tag <c>ready</c>.
/// </summary>
public sealed class SqlServerRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = "Server=localhost;Database=x;User Id=u;Password=p;TrustServerCertificate=true",
            ["Database:BulkInsertChunkSize"] = "5000"
        }).Build();

    [Fact]
    public void Vincula_options_e_registra_repositorios() {
        var services = new ServiceCollection().AddLogging().AddAedisSqlServer(Config());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.BulkInsertChunkSize.Should().Be(5000);
        services.Should().Contain(d => d.ServiceType == typeof(IRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IReadRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IWriteRepository<,>));
    }

    [Fact]
    public void Registra_health_check_sqlserver_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisSqlServer(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "sqlserver").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
