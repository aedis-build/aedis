using Aedis.Storage.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider FileSystem default (<see cref="DirectoryService" />).
/// </summary>
public static class DirectoryServiceCollectionExtensions
{
    /// <summary>
    ///     Registra um <see cref="IDirectory" /> controlando a pasta local <paramref name="basePath" />.
    /// </summary>
    public static IServiceCollection AddAedisDirectory(this IServiceCollection services, string basePath) {
        services.AddSingleton<IDirectory>(_ => new DirectoryService(basePath));
        return services;
    }

    /// <summary>
    ///     Registra um <see cref="IDirectory" /> com chave de serviço, para controlar várias pastas locais
    ///     (um <see cref="DirectoryService" /> por pasta).
    /// </summary>
    public static IServiceCollection AddAedisDirectory(this IServiceCollection services, object serviceKey,
        string basePath) {
        services.AddKeyedSingleton<IDirectory>(serviceKey, (_, _) => new DirectoryService(basePath));
        return services;
    }
}
