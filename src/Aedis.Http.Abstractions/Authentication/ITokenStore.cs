namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Armazenamento do token de acesso, com TTL. A implementação default é em memória (por processo); um
///     store distribuído (ex.: sobre o <c>ICache</c> do Aedis) permite compartilhar o token entre instâncias
///     e é opt-in. A expiração é governada pelo TTL: quando a entrada expira, <see cref="GetAsync" /> devolve
///     <c>null</c> e o token é renovado.
/// </summary>
public interface ITokenStore
{
    /// <summary>Lê o token armazenado para a <paramref name="key" />, ou <c>null</c> se ausente/expirado.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Grava o <paramref name="token" /> na <paramref name="key" /> com validade <paramref name="ttl" />.</summary>
    Task SetAsync(string key, string token, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Remove o token da <paramref name="key" /> (invalidação).</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
