using Aedis.Http.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Authentication;
using Aedis.Http.Native;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registra a camada HTTP do Aedis: a fábrica de clientes (provider nativo por default), o store de token
///     em memória (default) e integrações autenticadas nomeadas. Para trocar o provider, registre uma
///     <see cref="IAedisHttpClientFactory" /> de outro pacote (ex.: <c>Aedis.Http.Flurl</c>) antes; para
///     token distribuído, registre um <see cref="ITokenStore" /> de <c>Aedis.Http.Cache</c>.
/// </summary>
public static class HttpServiceCollectionExtensions
{
    /// <summary>
    ///     Registra os serviços base: <see cref="IAedisHttpClientFactory" /> nativo e
    ///     <see cref="ITokenStore" /> em memória, ambos com <c>TryAdd</c> (não sobrescrevem um provider ou
    ///     store já registrado).
    /// </summary>
    public static IServiceCollection AddAedisHttp(this IServiceCollection services) {
        services.TryAddSingleton<IAedisHttpClientFactory, NativeHttpClientFactory>();
        services.TryAddSingleton<ITokenStore, InMemoryTokenStore>();
        return services;
    }

    /// <summary>
    ///     Registra uma integração HTTP autenticada nomeada: um <see cref="ITokenProvider" /> e um
    ///     <see cref="IAedisHttpClient" /> autenticado (com Bearer + retry-on-401), ambos resolvíveis por
    ///     <paramref name="name" /> via serviços com chave. Garante a camada base com
    ///     <see cref="AddAedisHttp" />.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="name">Chave da integração (ex.: <c>"meu-provedor"</c>), usada para resolver o cliente.</param>
    /// <param name="configure">Configura o endpoint, a estratégia de credencial, o transporte (mTLS) e a chave de cache.</param>
    public static IServiceCollection AddAedisAuthenticatedClient(this IServiceCollection services, string name, Action<OAuthTokenOptions> configure) {
        services.AddAedisHttp();

        services.AddKeyedSingleton<ITokenProvider>(name, (provider, _) => {
            var options = BuildOptions(configure);
            return new OAuthTokenProvider(
                provider.GetRequiredService<IAedisHttpClientFactory>(),
                provider.GetRequiredService<ITokenStore>(),
                options,
                provider.GetService<TimeProvider>(),
                provider.GetService<ILogger<OAuthTokenProvider>>());
        });

        services.AddKeyedSingleton<IAedisHttpClient>(name, (provider, key) => {
            var options = BuildOptions(configure);
            var inner = provider.GetRequiredService<IAedisHttpClientFactory>().Create(options.Transport);
            var tokenProvider = provider.GetRequiredKeyedService<ITokenProvider>(key);
            return new AuthenticatedHttpClient(inner, tokenProvider);
        });

        return services;
    }

    private static OAuthTokenOptions BuildOptions(Action<OAuthTokenOptions> configure) {
        var options = new OAuthTokenOptions();
        configure(options);
        return options;
    }
}
