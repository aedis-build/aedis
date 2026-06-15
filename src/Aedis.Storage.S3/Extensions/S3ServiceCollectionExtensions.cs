using Aedis.Storage.Abstractions;
using Aedis.Storage.S3;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do provider AWS S3 do Aedis.
/// </summary>
public static class S3ServiceCollectionExtensions
{
    /// <summary>
    ///     Registra um bucket S3 como <see cref="IBucket{T}" />, onde <typeparamref name="T" /> é o tipo
    ///     concreto que herda <see cref="S3BucketService{T}" /> (um marcador por bucket).
    /// </summary>
    public static IServiceCollection AddAedisS3<T>(this IServiceCollection services, S3StorageOptions options)
        where T : S3BucketService<T> {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<T>(sp, options));
        services.AddSingleton<IBucket<T>>(sp => sp.GetRequiredService<T>());
        return services;
    }
}
