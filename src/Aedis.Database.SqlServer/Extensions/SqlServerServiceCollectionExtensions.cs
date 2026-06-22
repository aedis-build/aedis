using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer;
using Aedis.Database.SqlServer.Naming;
using Aedis.Database.SqlServer.TypeHandlers;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider SQL Server do Aedis.
/// </summary>
public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    ///     Registra a <see cref="IUnitOfWorkFactory" /> (sessões de escrita/leitura), as estratégias de
    ///     nomes e seu resolver, os type handlers de <see cref="DateOnly" />/<see cref="TimeOnly" /> do
    ///     Dapper e o motor de bulk insert via SqlBulkCopy (<see cref="SqlServerBulkInserter" />). Lê as
    ///     opções da seção <c>Database</c>. Também habilita o casamento de nomes com underscores no Dapper,
    ///     para mapear colunas <c>snake_case</c> em propriedades PascalCase.
    /// </summary>
    public static IServiceCollection AddAedisSqlServer(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton<INamingStrategy, SnakeCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, PascalCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, CamelCaseNamingStrategy>();
        services.TryAddSingleton(sp => new NamingStrategyResolver(sp.GetServices<INamingStrategy>()));

        services.TryAddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.TryAddSingleton<SqlServerBulkInserter>();

        services.TryAddScoped(typeof(IRepository<,>), typeof(SqlServerRepository<,>));
        services.TryAddScoped(typeof(IReadRepository<,>), typeof(SqlServerRepository<,>));
        services.TryAddScoped(typeof(IWriteRepository<,>), typeof(SqlServerRepository<,>));

        services.AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
