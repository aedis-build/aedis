using Aedis.Core.Extensions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Broker RabbitMQ. Publica mensagens serializadas pela estratégia adequada
///     (<see cref="MessageSerializerResolver" />) e consome via <see cref="RabbitMqConsumerManager" />,
///     que gerencia o ciclo de vida independente de cada consumer com retry/dead-letter.
/// </summary>
public class RabbitMqMessageBrokerService : RabbitMqBaseService, IMessageBrokerService
{
    private readonly RabbitMqConsumerManager _consumerManager;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly ILogger<RabbitMqMessageBrokerService> _logger;
    private readonly MessageSerializerResolver _serializers;
    private int _consecutiveUnhealthyChecks;

    /// <summary>
    ///     Cria o broker. Em geral é resolvido via DI, mas os parâmetros opcionais permitem injetar um
    ///     gerenciador de consumers e um resolvedor de serialização próprios (útil em testes); na ausência,
    ///     usa os padrões. O <paramref name="lifetime" /> habilita o self-heal por encerramento da aplicação.
    /// </summary>
    public RabbitMqMessageBrokerService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqMessageBrokerService> logger,
        RabbitMqConsumerManager? consumerManager = null,
        MessageSerializerResolver? serializers = null,
        IHostApplicationLifetime? lifetime = null)
        : base(options, logger) {
        _logger = logger;
        _serializers = serializers ?? MessageSerializerResolver.CreateDefault();
        _consumerManager = consumerManager
                           ?? new RabbitMqConsumerManager(NullLogger<RabbitMqConsumerManager>.Instance, options, _serializers);
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task PublishAsync<T>(string exchange, string routingKey, T message,
        CancellationToken cancellationToken = default)
        where T : class, IMessage {
        return ExecuteWithChannelAsync(async channel => {
            await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);

            var data = message.ToData();
            var serializer = _serializers.ResolveForSerialize(data);
            var body = serializer.Serialize(data);

            var props = new BasicProperties {
                Persistent = true,
                ContentType = serializer.ContentType,
                CorrelationId = message.CorrelationId
            };

            await channel.BasicPublishAsync(exchange, routingKey, false, props, body, cancellationToken);
            _logger.LogDebug("Mensagem {EventName} publicada em {Exchange}/{RoutingKey} ({ContentType})",
                message.EventName, exchange, routingKey, serializer.ContentType);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishRawAsync(string exchange, string routingKey, ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream", string? correlationId = null,
        CancellationToken cancellationToken = default) {
        return ExecuteWithChannelAsync(async channel => {
            await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);

            var props = new BasicProperties { Persistent = true, ContentType = contentType };
            if (correlationId is not null) props.CorrelationId = correlationId;

            await channel.BasicPublishAsync(exchange, routingKey, false, props, payload, cancellationToken);
            _logger.LogDebug("Payload bruto publicado em {Exchange}/{RoutingKey} ({ContentType})",
                exchange, routingKey, contentType);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeAsync<T>(string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(retryOptions);

        var connection = await GetConnectionAsync();
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try {
            await CreateOrGetExchangeAsync(exchange, channel, cancellationToken);
            await CreateOrGetQueueAsync(queue, exchange, routingKey, channel, cancellationToken);

            var queueName = queue.Sanitize().ToLowerInvariant();
            if (retryOptions.EnableDeadLetter)
                await EnsureFinalDeadLetterQueueAsync(queueName, channel, cancellationToken);

            var consumerId = await _consumerManager.StartConsumerAsync(channel, queueName, exchange, routingKey,
                handler, retryOptions, cancellationToken);

            await KeepConsumerAliveAsync(consumerId, cancellationToken);
        }
        catch (OperationCanceledException) {
        }
        finally {
            if (channel.IsOpen) await channel.CloseAsync(CancellationToken.None);
        }
    }

    /// <summary>Encerra todos os consumers ativos e, em seguida, libera os recursos da base (conexão e pool).</summary>
    public override async ValueTask DisposeAsync() {
        await _consumerManager.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    ///     Declara a DLQ final (<c>{fila}.dlq</c>) ligada ao exchange <c>default.dlq</c>, destino das
    ///     mensagens que excedem o limite de tentativas ou falham permanentemente.
    /// </summary>
    private async Task EnsureFinalDeadLetterQueueAsync(string queueName, IChannel channel,
        CancellationToken cancellationToken) {
        const string dlqExchange = "default.dlq";
        var dlqName = $"{queueName}.dlq";

        await channel.ExchangeDeclareAsync(dlqExchange, ExchangeType.Direct, true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(dlqName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(dlqName, dlqExchange, dlqName, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Mantém o consumer vivo: verifica a saúde a cada 60s e reinicia consumers não saudáveis.
    ///     Após falhas prolongadas, encerra a aplicação para que o orquestrador a reinicie (self-heal).
    /// </summary>
    private async Task KeepConsumerAliveAsync(string consumerId, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                if (!await _consumerManager.IsConsumerHealthyAsync(consumerId)) {
                    _logger.LogWarning("Consumer {ConsumerId} não está saudável; tentando reiniciar...", consumerId);
                    await _consumerManager.RestartUnhealthyConsumersAsync(cancellationToken);

                    if (++_consecutiveUnhealthyChecks >= 5) {
                        _logger.LogError(
                            "Consumer {ConsumerId} permaneceu não saudável. Encerrando a aplicação para reinício.",
                            consumerId);
                        _lifetime?.StopApplication();
                    }
                }
                else {
                    _consecutiveUnhealthyChecks = 0;
                }

                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro no laço de keep-alive do consumer {ConsumerId}.", consumerId);
        }
        finally {
            await _consumerManager.StopConsumerAsync(consumerId, CancellationToken.None);
        }
    }
}
