using Aedis.Core.Extensions;
using Aedis.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Base de conexão e pool de canais do RabbitMQ: gerencia uma conexão recuperável, um pool de
///     <see cref="IChannel" /> limitado e helpers para declarar exchanges/filas. O publish/consume
///     ficam nas classes derivadas.
/// </summary>
public abstract class RabbitMqBaseService : IAsyncDisposable
{
    private readonly ObjectPool<IChannel> _channelPool;
    private readonly SemaphoreSlim _channelSemaphore;
    private readonly TimeSpan _channelTimeout;
    private readonly Timer _connectionHealthTimer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ILogger<RabbitMqBaseService> _logger;
    protected readonly RabbitMqOptions _options;

    private IConnection? _connection;
    private volatile bool _isConnectionHealthy = true;
    internal int ChannelCount;

    protected RabbitMqBaseService(IOptions<RabbitMqOptions> options, ILogger<RabbitMqBaseService> logger) {
        _options = options.Value;
        _logger = logger;
        _channelTimeout = TimeSpan.FromSeconds(_options.ChannelTimeoutSeconds);
        _channelSemaphore = new SemaphoreSlim(_options.MaxChannels, _options.MaxChannels);
        _channelPool = new DefaultObjectPool<IChannel>(
            new RabbitMqChannelPooledPolicy(this, logger, _options), _options.MaxChannels);

        _connectionHealthTimer =
            new Timer(CheckConnectionHealth, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool IsConnectionHealthy => _isConnectionHealthy;

    public virtual async ValueTask DisposeAsync() {
        await _connectionHealthTimer.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _connectionLock.Dispose();
        _channelSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    public Task<IConnection> GetConnectionAsync() {
        return CreateOrGetConnectionAsync();
    }

    private async Task<IConnection> CreateOrGetConnectionAsync() {
        if (_connection is { IsOpen: true }) return _connection;

        await _connectionLock.WaitAsync();
        try {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = $"app:{ApplicationInfo.Name}"
            };

            _connection = await factory.CreateConnectionAsync();
            _logger.LogDebug("Nova conexão com RabbitMQ em {Host}:{Port}", _options.Host, _options.Port);
            return _connection;
        }
        finally {
            _connectionLock.Release();
        }
    }

    private IChannel GetChannelFromPool() => _channelPool.Get();
    private void ReturnChannelToPool(IChannel channel) => _channelPool.Return(channel);

    protected Task CreateOrGetExchangeAsync(string exchange, IChannel channel, CancellationToken cancellationToken) {
        return channel.ExchangeDeclareAsync(exchange.ToLowerInvariant(), ResolveExchangeType(exchange), true,
            cancellationToken: cancellationToken);
    }

    private Task BindQueueAsync(string queue, string exchange, string routingKey, IChannel channel,
        CancellationToken cancellationToken) {
        return channel.QueueBindAsync(queue.ToLowerInvariant(), exchange.ToLowerInvariant(), routingKey,
            cancellationToken: cancellationToken);
    }

    protected async Task CreateOrGetQueueAsync(string queue, string? exchange, string? routingKey, IChannel channel,
        CancellationToken cancellationToken) {
        var sanitizedQueue = queue.Sanitize().ToLowerInvariant();
        var queueArguments = BuildQueueArguments(null, null);

        if (string.IsNullOrEmpty(exchange) || string.IsNullOrEmpty(routingKey)) {
            await channel.QueueDeclareAsync(sanitizedQueue, true, false, false, queueArguments,
                cancellationToken: cancellationToken);
            return;
        }

        var createOrGetQueue = channel.QueueDeclareAsync(sanitizedQueue, true, false, false, queueArguments,
            cancellationToken: cancellationToken);
        var bindQueue = BindQueueAsync(sanitizedQueue, exchange, routingKey, channel, cancellationToken);

        await Task.WhenAll(createOrGetQueue, bindQueue);
    }

    protected Task ExecuteWithChannelAsync(Func<IChannel, Task> action, CancellationToken cancellationToken) {
        return ExecuteWithChannelInternalAsync<object?>(async channel => {
            await action(channel);
            return null;
        }, cancellationToken);
    }

    protected Task<T?> ExecuteWithChannelAsync<T>(Func<IChannel, Task<T?>> action, CancellationToken cancellationToken) {
        return ExecuteWithChannelInternalAsync<T>(
            channel => action(channel).ContinueWith(t => (object?)t.Result, cancellationToken), cancellationToken);
    }

    private async Task<T?> ExecuteWithChannelInternalAsync<T>(Func<IChannel, Task<object?>> action,
        CancellationToken cancellationToken) {
        var retry = 0;
        while (!cancellationToken.IsCancellationRequested) {
            if (await _channelSemaphore.WaitAsync(_channelTimeout, cancellationToken)) {
                var channel = GetChannelFromPool();
                try {
                    var result = await action(channel);
                    return (T?)result;
                }
                finally {
                    ReturnChannelToPool(channel);
                    _channelSemaphore.Release();
                }
            }

            retry++;
            var backoff = Math.Min(1000 * retry, 10000);
            _logger.LogDebug("Sem canais disponíveis. Aguardando {Delay}ms para tentar novamente...", backoff);
            await Task.Delay(backoff, cancellationToken);
        }

        _logger.LogDebug("Execução cancelada antes de obter um canal.");
        throw new OperationCanceledException();
    }

    private static string ResolveExchangeType(string exchange) {
        if (exchange.EndsWith(".fanout", StringComparison.OrdinalIgnoreCase)) return ExchangeType.Fanout;
        if (exchange.EndsWith(".direct", StringComparison.OrdinalIgnoreCase)) return ExchangeType.Direct;
        return ExchangeType.Topic;
    }

    private static Dictionary<string, object?> BuildQueueArguments(string? retryExchange, string? retryRoutingKey) {
        var arguments = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };

        if (string.IsNullOrWhiteSpace(retryExchange) || string.IsNullOrWhiteSpace(retryRoutingKey))
            return arguments;

        arguments["x-dead-letter-exchange"] = retryExchange;
        arguments["x-dead-letter-routing-key"] = retryRoutingKey;
        return arguments;
    }

    private void CheckConnectionHealth(object? state) {
        try {
            if (_connection is null || !_connection.IsOpen) {
                if (!_isConnectionHealthy) return;

                _logger.LogWarning("Conexão com o RabbitMQ não está saudável. Tentando reconectar...");
                _isConnectionHealthy = false;

                _ = Task.Run(async () => {
                    try {
                        await CreateOrGetConnectionAsync();
                        _isConnectionHealthy = true;
                        _logger.LogDebug("Conexão com o RabbitMQ restaurada com sucesso.");
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Falha ao restaurar a conexão com o RabbitMQ.");
                    }
                });
            }
            else if (!_isConnectionHealthy) {
                _logger.LogDebug("Conexão com o RabbitMQ saudável novamente.");
                _isConnectionHealthy = true;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao verificar a saúde da conexão com o RabbitMQ.");
        }
    }
}
