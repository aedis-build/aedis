namespace Aedis.Cache.Abstractions;

/// <summary>
///     Contrato agnóstico de provider para cache distribuído com eleição de líder. Expõe primitivas de
///     string com TTL, deduplicação atômica (<see cref="SetIfNotExistsAsync" />), contadores e locks
///     distribuídos (<see cref="IsLeaderAsync" />/<see cref="RenewLockAsync" />). Os serviços de mais alto
///     nível do Aedis (lote, execução) constroem-se apenas sobre estas operações; o provider (ex.: Redis)
///     fornece a implementação.
/// </summary>
public interface ICache
{
    /// <summary>
    ///     Tenta eleger esta instância como líder da <paramref name="key" /> adquirindo um lock distribuído
    ///     que expira em <paramref name="expiration" />. Use para garantir que só uma instância processe um
    ///     recurso por vez. Descartar o handle devolvido libera o lock.
    /// </summary>
    /// <returns>Handle de liderança a descartar para liberar o lock, ou <c>null</c> se outra instância já é líder.</returns>
    Task<IAsyncDisposable?> IsLeaderAsync(string key, TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>Lê o valor textual da <paramref name="key" />, ou <c>null</c> se a chave não existir.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Grava o <paramref name="value" /> na <paramref name="key" /> com TTL <paramref name="expiration" />, sobrescrevendo o valor existente.</summary>
    Task SetStringAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Grava o <paramref name="value" /> apenas se a <paramref name="key" /> ainda não existir (semântica
    ///     <c>SET NX</c>), com TTL <paramref name="expiration" />. Primitiva atômica de deduplicação.
    /// </summary>
    /// <returns><c>true</c> se gravou (chave nova); <c>false</c> se a chave já existia.</returns>
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>Indica se a <paramref name="key" /> existe no cache.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Remove a <paramref name="key" />; devolve <c>true</c> se a chave existia e foi apagada.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Estende o TTL de um lock de liderança existente, prorrogando a posse sem reeleger. Use
    ///     periodicamente enquanto a instância segue líder de um trabalho longo.
    /// </summary>
    /// <returns><c>true</c> se o lock existia e o TTL foi renovado; <c>false</c> se não havia lock.</returns>
    Task<bool> RenewLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Devolve as chaves que casam com <paramref name="pattern" /> (glob). Operação de scan — evite
    ///     padrões muito abrangentes em bases grandes pelo custo de varredura.
    /// </summary>
    Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Incrementa atomicamente um contador e devolve o novo valor. No primeiro incremento (retorno == 1),
    ///     define o TTL <paramref name="ttl" /> da chave.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}