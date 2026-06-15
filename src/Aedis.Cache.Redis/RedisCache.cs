using Aedis.Cache.Abstractions;
using Aedis.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Aedis.Cache.Redis;

/// <summary>
///     Implementação de <see cref="ICache" /> sobre o Redis (StackExchange.Redis). Mantém uma única
///     conexão multiplexada por processo, com reconexão automática, e oferece locks distribuídos via
///     <see cref="IsLeaderAsync" /> (eleição de líder). É registrada como singleton por
///     <c>AddAedisRedis()</c> — o provider é o pacote de implementação por trás do contrato.
/// </summary>
public sealed class RedisCache : ICache
{
    private readonly string _instanceId;
    private readonly object _lock = new();
    private readonly ILogger<RedisCache> _logger;
    private readonly RedisCacheOptions _options;

    private IConnectionMultiplexer _connection;

    public RedisCache(IOptions<RedisCacheOptions> options, ILogger<RedisCache> logger) {
        _options = options.Value;
        _logger = logger;
        _instanceId = string.IsNullOrWhiteSpace(_options.InstanceId) ? Environment.MachineName : _options.InstanceId;
        _connection = Connect();
        _logger.LogDebug("Conectado ao Redis em {EndPoint}.", _options.EndPoint);
    }

    /// <summary>Banco do Redis na conexão ativa (reconecta se necessário). Uso interno/diagnóstico.</summary>
    internal IDatabase Database => EnsureConnection().GetDatabase();

    public async Task<IAsyncDisposable?> IsLeaderAsync(string key, TimeSpan expiration,
        CancellationToken cancellationToken = default) {
        var formattedKey = FormatLockKey(key);

        try {
            var acquired = await Database.LockTakeAsync(formattedKey, _instanceId, expiration).ConfigureAwait(false);

            if (acquired) {
                _logger.LogDebug("Lock adquirido para a chave {Key} pela instância {InstanceId}.",
                    formattedKey, _instanceId);
                return new RedisLock(Database, formattedKey, _instanceId, _logger);
            }

            // Já existe um lock: se for desta própria instância (ex.: re-entrância após falha de rede),
            // devolve um handle; caso contrário, não é o líder.
            var currentLockOwner = await GetStringAsync(formattedKey, cancellationToken).ConfigureAwait(false);

            return currentLockOwner != _instanceId
                ? null
                : new RedisLock(Database, formattedKey, _instanceId, _logger);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao tentar adquirir o lock para a chave {Key}.", formattedKey);
            return null;
        }
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) {
        var value = await Database.StringGetAsync(key).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetStringAsync(string key, string value, TimeSpan expiration,
        CancellationToken cancellationToken = default) {
        await Database.StringSetAsync(key, value, expiration).ConfigureAwait(false);
    }

    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration,
        CancellationToken cancellationToken = default) {
        var wasSet = await Database.StringSetAsync(key, value, expiration, When.NotExists).ConfigureAwait(false);

        if (wasSet)
            _logger.LogDebug("Chave definida com sucesso (NX): {Key}", key);
        else
            _logger.LogTrace("Chave já existe (NX falhou): {Key}", key);

        return wasSet;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) {
        return await Database.KeyExistsAsync(key).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) {
        return await Database.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    public async Task<bool> RenewLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) {
        var formattedKey = FormatLockKey(key);

        try {
            var exists = await Database.KeyExistsAsync(formattedKey).ConfigureAwait(false);
            if (!exists) {
                _logger.LogDebug("Não é possível renovar o lock da chave {Key} — o lock não existe.", formattedKey);
                return false;
            }

            await Database.KeyExpireAsync(formattedKey, ttl).ConfigureAwait(false);
            _logger.LogDebug("Lock renovado para a chave {Key} com TTL {Ttl}.", formattedKey, ttl);
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao renovar o lock da chave {Key}.", formattedKey);
            return false;
        }
    }

    public Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default) {
        try {
            var connection = EnsureConnection();
            var endPoint = connection.GetEndPoints().FirstOrDefault();

            if (endPoint is null) {
                _logger.LogWarning("Nenhum endpoint Redis disponível para o scan de chaves.");
                return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
            }

            var server = connection.GetServer(endPoint);
            var keys = server.Keys(pattern: pattern).Select(k => k.ToString()).ToList();

            _logger.LogDebug("Encontradas {Count} chaves para o padrão '{Pattern}'.", keys.Count, pattern);
            return Task.FromResult<IEnumerable<string>>(keys);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao obter chaves para o padrão '{Pattern}'.", pattern);
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) {
        var value = await Database.StringIncrementAsync(key).ConfigureAwait(false);
        if (value == 1)
            await Database.KeyExpireAsync(key, ttl).ConfigureAwait(false);
        return value;
    }

    private IConnectionMultiplexer EnsureConnection() {
        lock (_lock) {
            if (_connection.IsConnected) {
                _logger.LogDebug("Reutilizando a conexão existente do Redis.");
                return _connection;
            }

            try {
                _connection.Dispose();
                _connection = Connect();
                _logger.LogDebug("Reconectado ao Redis em {EndPoint}.", _options.EndPoint);
                return _connection;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Falha ao tentar reconectar ao Redis.");
                throw new RedisConnectionException(ConnectionFailureType.None, "Falha ao tentar reconectar ao Redis.");
            }
        }
    }

    private IConnectionMultiplexer Connect() {
        var configOptions = new ConfigurationOptions {
            ServiceName = _options.SentinelMasterName,
            EndPoints = { _options.EndPoint },
            CommandMap = CommandMap.Default,
            TieBreaker = "",
            AbortOnConnectFail = false,
            ConnectRetry = 5,
            ConnectTimeout = 5000,
            ReconnectRetryPolicy = new ExponentialRetry(5000),
            User = _options.User,
            Password = _options.Password,
            DefaultDatabase = 0,
            AllowAdmin = false,
            KeepAlive = 60,
            Ssl = _options.UseSsl
        };

        var connection = ConnectionMultiplexer.Connect(configOptions);
        connection.ConnectionFailed += OnConnectionFailed;
        connection.ConnectionRestored += OnConnectionRestored;

        EnsureEndpointsReachable(connection);

        return connection;
    }

    private void EnsureEndpointsReachable(IConnectionMultiplexer connection) {
        var endpoints = connection.GetEndPoints();
        if (endpoints is null or { Length: 0 })
            throw new InvalidOperationException("Não foi possível conectar ao Redis: nenhum endpoint ativo.");

        _logger.LogDebug("Redis conectado. Endpoints ativos: {Endpoints}", endpoints);
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e) {
        _logger.LogWarning("Conexão com o Redis falhou: {Endpoint}. Tipo da falha: {FailureType}",
            e.EndPoint, e.FailureType);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e) {
        _logger.LogDebug("Conexão com o Redis restaurada: {Endpoint}", e.EndPoint);
    }

    private static string FormatLockKey(string key) {
        return $"{ApplicationInfo.Name}:lock:{key.ToLowerInvariant()}";
    }
}
