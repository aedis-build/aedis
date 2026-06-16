using Aedis.Database.Abstractions;
using Aedis.Database.Postgres;
using Aedis.Database.Postgres.Naming;
using Aedis.Database.Postgres.TypeHandlers;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider PostgreSQL do Aedis.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    ///     Registra a <see cref="IUnitOfWorkFactory" /> (sessões de escrita/leitura), as estratégias de
    ///     nomes e seu resolver, os type handlers de <see cref="DateOnly" />/<see cref="TimeOnly" /> do
    ///     Dapper e o motor de bulk insert via COPY (<see cref="PostgresBulkInserter" />). Lê as opções da
    ///     seção <c>Database</c>.
    /// </summary>
    public static IServiceCollection AddAedisPostgres(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true; // mapeia colunas snake_case → propriedades PascalCase

        services.AddSingleton<INamingStrategy, SnakeCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, PascalCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, CamelCaseNamingStrategy>();
        services.TryAddSingleton(sp => new NamingStrategyResolver(sp.GetServices<INamingStrategy>()));

        services.TryAddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.TryAddSingleton<PostgresBulkInserter>();

        services.TryAddScoped(typeof(IRepository<,>), typeof(PostgresRepository<,>));
        services.TryAddScoped(typeof(IReadRepository<,>), typeof(PostgresRepository<,>));
        services.TryAddScoped(typeof(IWriteRepository<,>), typeof(PostgresRepository<,>));

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
