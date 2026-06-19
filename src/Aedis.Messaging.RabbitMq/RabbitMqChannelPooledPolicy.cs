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

    /// <summary>
    ///     Cria a política e aquece a conexão subjacente (resolvida de forma síncrona no construtor) para que o
    ///     pool possa criar canais sem latência adicional na primeira aquisição.
    /// </summary>
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

    /// <summary>
    ///     Cria um novo canal sobre a conexão, aplica o QoS/prefetch configurado e registra o handler de
    ///     mensagens devolvidas (basic.return). Invocado pelo pool quando não há canal ocioso.
    /// </summary>
    public IChannel Create() {
        var connection = _cachedConnectionTask.GetAwaiter().GetResult();
        var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

        channel.BasicQosAsync(0, _options.PrefetchCount, false).Wait();
        channel.BasicReturnAsync += _basicReturnHandler;

        var current = Interlocked.Increment(ref _baseService.ChannelCount);
        _logger.LogDebug("Canal criado pelo pool. Total atual: {Count}", current);
        return channel;
    }

    /// <summary>
    ///     Decide se o canal volta ao pool: mantém-no se ainda estiver aberto; caso contrário, descarta-o,
    ///     atualizando a contagem de canais. Retorna <c>true</c> quando o canal é reaproveitado.
    /// </summary>
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
