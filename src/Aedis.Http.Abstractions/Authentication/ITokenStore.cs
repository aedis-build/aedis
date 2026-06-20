namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Armazenamento do token de acesso (com seus instantes de expiração e renovação) e do lock de geração.
///     A implementação default é em memória (por processo); um store distribuído (ex.: sobre o <c>ICache</c>
///     do Aedis) compartilha o token e o lock entre instâncias, sendo opt-in. O lock de geração
///     (<see cref="TryAcquireRefreshLockAsync" />) coordena quem busca/renova o token, evitando que várias
///     chamadas ou instâncias o gerem ao mesmo tempo (race condition).
/// </summary>
public interface ITokenStore
{
    /// <summary>Lê o token armazenado para a <paramref name="key" />, ou <c>null</c> se ausente/expirado.</summary>
    Task<CachedToken?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Grava o <paramref name="token" /> na <paramref name="key" />. A validade da entrada segue o
    ///     <see cref="CachedToken.ExpiresAt" /> do token.
    /// </summary>
    Task SetAsync(string key, CachedToken token, CancellationToken cancellationToken = default);

    /// <summary>Remove o token da <paramref name="key" /> (invalidação).</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tenta adquirir o lock de geração/renovação do token da <paramref name="key" />, expirando em
    ///     <paramref name="ttl" /> (auto-liberação se o detentor falhar). Devolve um handle a descartar para
    ///     liberar o lock, ou <c>null</c> se outra chamada/instância já o detém — nesse caso, o chamador não
    ///     deve gerar o token, e sim aguardar/servir o token atual.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireRefreshLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}
