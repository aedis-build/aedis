using Aedis.Storage.Abstractions;
using Aedis.Storage.AzureBlob;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider Azure Blob Storage do Aedis.
/// </summary>
public static class AzureBlobServiceCollectionExtensions
{
    /// <summary>
    ///     Registra um container Azure Blob como <see cref="IBucket{T}" />, onde <typeparamref name="T" /> é o
    ///     tipo concreto que herda <see cref="AzureBlobBucketService{T}" /> (um marcador por container).
    /// </summary>
    public static IServiceCollection AddAedisAzureBlob<T>(this IServiceCollection services,
        AzureBlobStorageOptions options)
        where T : AzureBlobBucketService<T> {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<T>(sp, options));
        services.AddSingleton<IBucket<T>>(sp => sp.GetRequiredService<T>());
        return services;
    }
}
