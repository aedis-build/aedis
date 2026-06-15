using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Política de pool de canais (<see cref="IChannel" />) do RabbitMQ: cria canais com QoS/prefetch
///     configurados e devolve/descarta conforme a saúde do canal.
/// </summary>
public class RabbitMqChannelPooledPolicy : IPooledObjectPolicy<IChannel>
{
    private readonly RabbitMqBaseService _baseService;
    private readonly AsyncEventHandler<BasicReturnEventArgs> _basicReturnHandler;
    private readonly Task<IConnection> _cachedConnectionTask;
    private readonly ILogger _logger;
    private readonly RabbitMqOptions _options;

    public RabbitMqChannelPooledPolicy(RabbitMqBaseService baseService, ILogger logger, RabbitMqOptions options) {
        _baseService = baseService;
        _logger = logger;
        _options = options;
        _cachedConnectionTask = _baseService.GetConnectionAsync();
        _cachedConnectionTask.GetAwaiter().GetResult();
        _basicReturnHandler = (sender, e) => {
            _logger.LogError("Falha ao entregar mensagem na {Exchange} com routing key {RoutingKey}", e.Exchange,
                e.RoutingKey);
            return Task.CompletedTask;
        };
    }

    public IChannel Create() {
        var connection = _cachedConnectionTask.GetAwaiter().GetResult();
        var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

        channel.BasicQosAsync(0, _options.PrefetchCount, false).Wait();
        channel.BasicReturnAsync += _basicReturnHandler;

        var current = Interlocked.Increment(ref _baseService.ChannelCount);
        _logger.LogDebug("Canal criado pelo pool. Total atual: {Count}", current);
        return channel;
    }

    public bool Return(IChannel obj) {
        if (obj.IsOpen) {
            _logger.LogDebug("Canal retornado ao pool.");
            return true;
        }

        Interlocked.Decrement(ref _baseService.ChannelCount);
        obj.DisposeAsync().AsTask().Wait();
        _logger.LogDebug("Canal descartado Total: {ChannelCount}", _baseService.ChannelCount);
        return false;
    }
}
