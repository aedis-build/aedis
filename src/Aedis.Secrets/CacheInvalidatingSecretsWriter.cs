using Aedis.Secrets.Abstractions;

namespace Aedis.Secrets;

/// <summary>
///     Decorator de <see cref="ISecretsWriter" /> que, após cada escrita ou remoção bem-sucedida no cofre,
///     invalida a entrada correspondente do <see cref="CachingSecretsProvider" /> em processo. Garante que a
///     própria instância que rotacionou um segredo passe a ler o valor novo imediatamente, em vez de esperar
///     o TTL do cache expirar.
/// </summary>
public sealed class CacheInvalidatingSecretsWriter : ISecretsWriter
{
    private readonly CachingSecretsProvider _cache;
    private readonly ISecretsWriter _inner;

    /// <summary>Cria o decorator sobre o writer <paramref name="inner" /> e o cache <paramref name="cache" /> a invalidar.</summary>
    public CacheInvalidatingSecretsWriter(ISecretsWriter inner, CachingSecretsProvider cache) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default) {
        await _inner.SetSecretAsync(name, value, cancellationToken);
        _cache.Invalidate(name);
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default) {
        await _inner.DeleteSecretAsync(name, cancellationToken);
        _cache.Invalidate(name);
    }
}
