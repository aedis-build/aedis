using Aedis.Hosting.AspNetCore.Hypermedia;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Ponto de entrada do registro de hipermídia. Por tipo de resposta, declara o provedor de links
///     (<c>IResourceLinks&lt;T&gt;</c>) que serve tanto ao recurso único quanto a cada item de uma coleção. O
///     registro é explícito e determinístico (sem varredura por reflexão); provedores são <c>scoped</c> porque
///     dependem do contexto da requisição.
/// </summary>
public static class HypermediaServiceCollectionExtensions {
    /// <summary>
    ///     Inicia o registro fluente de hipermídia.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IAedisHypermediaBuilder AddAedisHypermedia(this IServiceCollection services) {
        return new AedisHypermediaBuilder(services);
    }
}

/// <summary>
///     Builder fluente para registrar os provedores de links de hipermídia por tipo de resposta.
/// </summary>
public interface IAedisHypermediaBuilder {
    /// <summary>
    ///     Coleção de serviços subjacente.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    ///     Registra o provedor de links de um tipo de resposta. Aplica-se ao recurso único e a cada item de
    ///     coleção desse tipo.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta.</typeparam>
    /// <typeparam name="TLinks">Provedor de links (tipicamente uma subclasse de <c>ResourceLinks&lt;T&gt;</c>).</typeparam>
    IAedisHypermediaBuilder Resource<TResponse, TLinks>() where TLinks : class, Aedis.Hosting.AspNetCore.Hypermedia.IResourceLinks<TResponse>;
}

internal sealed class AedisHypermediaBuilder : IAedisHypermediaBuilder {
    public AedisHypermediaBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public IAedisHypermediaBuilder Resource<TResponse, TLinks>() where TLinks : class, Aedis.Hosting.AspNetCore.Hypermedia.IResourceLinks<TResponse> {
        Services.AddScoped<Aedis.Hosting.AspNetCore.Hypermedia.IResourceLinks<TResponse>, TLinks>();
        return this;
    }
}
