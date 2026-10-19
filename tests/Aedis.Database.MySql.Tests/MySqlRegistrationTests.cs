using Aedis.Database.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     <c>AddAedisMySql()</c> sem banco: vínculo de options, registro dos repositórios (open generic), da
///     fábrica de sessões e do health check <c>mysql</c> com a tag <c>ready</c>; a fábrica falha claro sem
///     connection string.
/// </summary>
public sealed class MySqlRegistrationTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:ConnectionString"] = "Server=localhost;Database=x;Uid=u;Pwd=p",
            ["Database:BulkInsertChunkSize"] = "5000"
        }).Build();

    [Fact]
    public void Vincula_options_e_registra_repositorios() {
        var services = new ServiceCollection().AddLogging().AddAedisMySql(Config());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.BulkInsertChunkSize.Should().Be(5000);
        provider.GetRequiredService<IUnitOfWorkFactory>().Should().BeOfType<UnitOfWorkFactory>();
        services.Should().Contain(d => d.ServiceType == typeof(IRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IReadRepository<,>));
        services.Should().Contain(d => d.ServiceType == typeof(IWriteRepository<,>));
    }

    [Fact]
    public void Registra_health_check_mysql_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisMySql(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "mysql").Subject;

        registration.Tags.Should().Contain("ready");
    }

    [Fact]
    public async Task Fabrica_sem_connection_string_falha_claro_nas_duas_sessoes() {
        var provider = new ServiceCollection().AddLogging().AddAedisMySql(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();

        await FluentActions.Awaiting(() => factory.CreateWriteSessionAsync()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => factory.CreateReadSessionAsync()).Should().ThrowAsync<InvalidOperationException>();
    }
}
