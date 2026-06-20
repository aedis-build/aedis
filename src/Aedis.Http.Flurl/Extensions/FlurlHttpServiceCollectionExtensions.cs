using Aedis.Http.Abstractions;
using Aedis.Http.Flurl;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra o Flurl como provider de <see cref="IAedisHttpClient" />, substituindo a fábrica nativa.
///     Combine com <c>AddAedisHttp</c>/<c>AddAedisAuthenticatedClient</c> do pacote <c>Aedis.Http</c>, que
///     fornecem o store de token e o template autenticado — a ordem de chamada não importa (o Flurl prevalece).
/// </summary>
public static class FlurlHttpServiceCollectionExtensions
{
    /// <summary>
    ///     Troca a <see cref="IAedisHttpClientFactory" /> registrada pela implementação Flurl
    ///     (<see cref="FlurlHttpClientFactory" />).
    /// </summary>
    public static IServiceCollection AddAedisHttpFlurl(this IServiceCollection services) {
        services.Replace(ServiceDescriptor.Singleton<IAedisHttpClientFactory, FlurlHttpClientFactory>());
        return services;
    }
}
