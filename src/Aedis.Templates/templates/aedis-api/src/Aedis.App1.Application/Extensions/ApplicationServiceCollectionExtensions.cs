using Aedis.App1.Application.Products.Commands;
using Aedis.App1.Application.Products.Commands.Handlers;
using Aedis.App1.Application.Products.Queries;
using Aedis.App1.Application.Products.Queries.Handlers;
using Aedis.App1.Domain.Entities;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro da camada de aplicação: o executor CQRS e cada handler de comando/consulta. Mantém o
///     <c>composition root</c> da API enxuto — a API só chama <c>AddApplication</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions {
    /// <summary>
    ///     Registra o executor de comandos e todos os handlers da aplicação.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services) {
        services.AddAedisCommands();

        services.AddAedisCommandHandler<CreateProductCommand, Product, CreateProductCommandHandler>();
        services.AddAedisCommandHandler<UpdateProductCommand, Product, UpdateProductCommandHandler>();
        services.AddAedisCommandHandler<DeleteProductCommand, DeleteProductResult, DeleteProductCommandHandler>();
        services.AddAedisCommandHandler<GetProductByIdQuery, Product?, GetProductByIdQueryHandler>();
        services.AddAedisCommandHandler<SearchProductsQuery, SearchProductsResult, SearchProductsQueryHandler>();

        return services;
    }
}
