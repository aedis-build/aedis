using System.Diagnostics;
using System.Net;
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
    private static readonly TimeSpan StaleConnectionDisposeDelay = TimeSpan.FromSeconds(30);
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
                // Sem race: a conexão antiga só é liberada depois de um atraso, evitando descartar uma
                // conexão que operações em voo ainda possam estar usando ao trocar de multiplexer.
                var previousConnection = _connection;
                _connection = Connect();
                ScheduleDeferredDispose(previousConnection);

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
        // Standalone/Cluster: conexão direta (o driver detecta o cluster). Sentinel: descobre o master.
        var connection = string.IsNullOrWhiteSpace(_options.SentinelMasterName)
            ? ConnectDirect(_options.EndPoint)
            : ConnectViaSentinel();

        connection.ConnectionFailed += OnConnectionFailed;
        connection.ConnectionRestored += OnConnectionRestored;

        EnsureEndpointsReachable(connection);

        return connection;
    }

    private IConnectionMultiplexer ConnectDirect(string endpoint) {
        var configOptions = new ConfigurationOptions {
            EndPoints = { endpoint },
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

        return ConnectionMultiplexer.Connect(configOptions);
    }

    private IConnectionMultiplexer ConnectViaSentinel() {
        var masterEndpoint = DiscoverMasterEndpointViaSentinel();
        _logger.LogDebug("Master Redis descoberto via Sentinel: {MasterEndpoint}", masterEndpoint);
        return ConnectDirect(masterEndpoint);
    }

    private string DiscoverMasterEndpointViaSentinel() {
        var serviceName = _options.SentinelMasterName!;

        var sentinelOptions = new ConfigurationOptions {
            EndPoints = { _options.EndPoint },
            ServiceName = serviceName,
            CommandMap = CommandMap.Sentinel,
            TieBreaker = "",
            AbortOnConnectFail = false,
            ConnectRetry = 1,
            ConnectTimeout = 1500,
            ReconnectRetryPolicy = new ExponentialRetry(1000),
            User = _options.SentinelUser,
            Password = _options.SentinelPassword,
            DefaultDatabase = 0,
            AllowAdmin = true,
            KeepAlive = 60,
            HeartbeatConsistencyChecks = false,
            Ssl = _options.UseSsl
        };

        using var sentinelConnection = ConnectionMultiplexer.SentinelConnect(sentinelOptions);

        var sentinelEndpoint = sentinelConnection.GetEndPoints().FirstOrDefault()
                               ?? throw new InvalidOperationException("Não foi possível conectar ao Redis Sentinel.");

        var masterEndpoint = sentinelConnection.GetServer(sentinelEndpoint)
            .SentinelGetMasterAddressByName(serviceName);

        var resolved = masterEndpoint?.ToString()
                       ?? throw new RedisConnectionException(ConnectionFailureType.UnableToResolvePhysicalConnection,
                           $"Sentinel devolveu um endpoint de master inválido para '{serviceName}'.");

        return RewriteMasterEndpointForLocalSentinel(NormalizeEndpoint(resolved));
    }

    /// <summary>
    ///     Em dev local com Sentinel/Redis em Docker: se o Sentinel é local (loopback) mas devolve um IP
    ///     interno do Docker para o master, reescreve para <c>127.0.0.1</c> mantendo a porta, para o host
    ///     conseguir conectar.
    /// </summary>
    private string RewriteMasterEndpointForLocalSentinel(string resolvedMasterEndpoint) {
        if (!TryParseHostPort(_options.EndPoint, out var sentinelHost, out _) || !IsLoopbackHost(sentinelHost))
            return resolvedMasterEndpoint;

        if (!TryParseHostPort(resolvedMasterEndpoint, out var masterHost, out var masterPort)
            || IsLoopbackHost(masterHost))
            return resolvedMasterEndpoint;

        var isDockerInternalHost = IsDockerInternalHost(masterHost);
        IPAddress? ipAddress = null;
        if (!isDockerInternalHost)
            IPAddress.TryParse(masterHost, out ipAddress);

        if (!isDockerInternalHost && ipAddress is null)
            return resolvedMasterEndpoint;
        if (ipAddress is not null && !IsPrivateDockerIp(ipAddress))
            return resolvedMasterEndpoint;

        var rewritten = $"127.0.0.1:{masterPort}";
        _logger.LogWarning(
            "Sentinel local devolveu master interno ({MasterEndpoint}); reescrevendo para {RewrittenEndpoint}.",
            resolvedMasterEndpoint, rewritten);
        return rewritten;
    }

    private void ScheduleDeferredDispose(IConnectionMultiplexer staleConnection) {
        _ = Task.Run(async () => {
            try {
                await Task.Delay(StaleConnectionDisposeDelay).ConfigureAwait(false);
                staleConnection.Dispose();
            }
            catch (Exception ex) {
                _logger.LogDebug(ex, "Falha ao liberar a conexão Redis antiga.");
            }
        });
    }

    private static string NormalizeEndpoint(string endpoint) =>
        TryParseHostPort(endpoint, out var host, out var port) ? $"{host}:{port}" : endpoint;

    private static bool TryParseHostPort(string endpoint, out string host, out int port) {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.Contains(':'))
            return false;

        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == endpoint.Length - 1)
            return false;

        host = endpoint[..lastColon].Trim('[', ']');
        var slash = host.LastIndexOf('/');
        if (slash >= 0 && slash < host.Length - 1)
            host = host[(slash + 1)..];

        return int.TryParse(endpoint[(lastColon + 1)..], out port);
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

    private static bool IsDockerInternalHost(string host) =>
        string.Equals(host, "redis", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "host.docker.internal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "gateway.docker.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateDockerIp(IPAddress ipAddress) {
        var bytes = ipAddress.GetAddressBytes();
        if (bytes.Length != 4) return false;
        return bytes[0] == 10
               || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
               || (bytes[0] == 192 && bytes[1] == 168);
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
