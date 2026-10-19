using Aedis.Database;
using Aedis.Database.Abstractions;
using Aedis.Database.MySql;
using Aedis.Database.MySql.TypeHandlers;
using Aedis.Database.Naming;
using Aedis.Database.TypeHandlers;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider MySQL do Aedis.
/// </summary>
public static class MySqlServiceCollectionExtensions
{
    /// <summary>
    ///     Registra a <see cref="IUnitOfWorkFactory" /> (sessões de escrita/leitura), as estratégias de
    ///     nomes e seu resolver, os type handlers de <see cref="DateOnly" />/<see cref="TimeOnly" />/
    ///     <see cref="DateTimeOffset" /> e de dicionários <c>string → string</c> (coluna <c>JSON</c>) do
    ///     Dapper e o motor de bulk insert por INSERT multi-linha (<see cref="MySqlBulkInserter" />). Lê as
    ///     opções da seção <c>Database</c>. Também habilita o casamento de nomes com underscores no Dapper,
    ///     para mapear colunas <c>snake_case</c> em propriedades PascalCase.
    /// </summary>
    public static IServiceCollection AddAedisMySql(this IServiceCollection services,
        IConfiguration configuration) {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        SqlMapper.AddTypeHandler(new JsonDictionaryTypeHandler<Dictionary<string, string?>>());
        SqlMapper.AddTypeHandler(new JsonDictionaryTypeHandler<IDictionary<string, string?>>());
        SqlMapper.AddTypeHandler(new JsonDictionaryTypeHandler<IReadOnlyDictionary<string, string?>>());
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton<INamingStrategy, SnakeCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, PascalCaseNamingStrategy>();
        services.AddSingleton<INamingStrategy, CamelCaseNamingStrategy>();
        services.TryAddSingleton(sp => new NamingStrategyResolver(sp.GetServices<INamingStrategy>()));

        services.TryAddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.TryAddSingleton<MySqlBulkInserter>();

        services.TryAddScoped(typeof(IRepository<,>), typeof(MySqlRepository<,>));
        services.TryAddScoped(typeof(IReadRepository<,>), typeof(MySqlRepository<,>));
        services.TryAddScoped(typeof(IWriteRepository<,>), typeof(MySqlRepository<,>));

        services.AddHealthChecks()
            .AddCheck<MySqlHealthCheck>("mysql", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));

        return services;
    }
}
