using Aedis.App1.Application.Abstractions;
using Aedis.App1.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro da camada de infraestrutura: o provider PostgreSQL do Aedis (que registra o repositório
///     genérico, a unidade de trabalho e a auditoria) e os repositórios concretos do domínio.
/// </summary>
public static class InfrastructureServiceCollectionExtensions {
    /// <summary>
    ///     Registra o acesso a dados PostgreSQL e os repositórios da aplicação.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação (seção <c>Database</c>).</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) {
        services.AddAedisPostgres(configuration);
        services.AddScoped<IProductRepository, ProductRepository>();
        return services;
    }
}
