using Aedis.Hosting.AspNetCore.Hateoas;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro explícito dos builders de links HATEOAS. O registro é por tipo e determinístico (sem varredura
///     por reflexão): cada tipo de resposta declara seu builder de item único e, opcionalmente, o de coleção
///     paginada. Builders são <c>scoped</c> porque podem depender do contexto da requisição (URL base, usuário).
/// </summary>
public static class HateoasServiceCollectionExtensions {
    /// <summary>
    ///     Registra o builder de links de item único de um tipo de resposta.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta.</typeparam>
    /// <typeparam name="TBuilder">Implementação de <see cref="IResourceLinkBuilder{T}" />.</typeparam>
    public static IServiceCollection AddAedisResourceLinks<TResponse, TBuilder>(this IServiceCollection services)
        where TBuilder : class, IResourceLinkBuilder<TResponse> {
        services.AddScoped<IResourceLinkBuilder<TResponse>, TBuilder>();
        return services;
    }

    /// <summary>
    ///     Registra a paginação padrão (<see cref="DefaultCollectionLinkBuilder{T}" />) para a coleção de um
    ///     tipo de resposta, usando o caminho base informado para montar os links de navegação.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de cada item da coleção.</typeparam>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="basePath">Caminho base da coleção (por exemplo, <c>/v1/products</c>).</param>
    public static IServiceCollection AddAedisCollectionLinks<TResponse>(this IServiceCollection services, string basePath) {
        services.AddScoped<ICollectionLinkBuilder<TResponse>>(_ => new DefaultCollectionLinkBuilder<TResponse>(basePath));
        return services;
    }

    /// <summary>
    ///     Registra um builder de coleção customizado para um tipo de resposta, quando a paginação padrão não
    ///     atende.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de cada item da coleção.</typeparam>
    /// <typeparam name="TBuilder">Implementação de <see cref="ICollectionLinkBuilder{T}" />.</typeparam>
    public static IServiceCollection AddAedisCollectionLinks<TResponse, TBuilder>(this IServiceCollection services)
        where TBuilder : class, ICollectionLinkBuilder<TResponse> {
        services.AddScoped<ICollectionLinkBuilder<TResponse>, TBuilder>();
        return services;
    }
}
