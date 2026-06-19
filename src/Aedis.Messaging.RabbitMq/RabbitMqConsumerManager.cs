using System.Collections.Concurrent;
using System.Diagnostics;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Gerencia o ciclo de vida independente de cada consumer (registro, monitor, parada e verificação de
///     saúde) e o despacho de retry/dead-letter por tipo de exceção, com filas de delay via TTL.
/// </summary>
public class RabbitMqConsumerManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConsumerInfo> _activeConsumers = new();
    private readonly SemaphoreSlim _disposeSemaphore = new(1, 1);
    private readonly ILogger<RabbitMqConsumerManager> _logger;
    private readonly RabbitMqOptions _options;
    private readonly MessageSerializerResolver _serializers;
    private bool _disposed;

    /// <summary>Cria o gerenciador com o logger, as opções do RabbitMQ e o resolvedor de serialização usados ao consumir.</summary>
    public RabbitMqConsumerManager(ILogger<RabbitMqConsumerManager> logger, IOptions<RabbitMqOptions> options,
        MessageSerializerResolver serializers) {
        _logger = logger;
        _options = options.Value;
        _serializers = serializers;
    }

    /// <summary>Para todos os consumers ativos e libera os recursos. Idempotente e protegido contra chamadas concorrentes.</summary>
    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        await _disposeSemaphore.WaitAsync();
        try {
            if (_disposed) return;

            _logger.LogDebug("Encerrando {Count} consumers RabbitMQ ativos.", _activeConsumers.Count);
            await Task.WhenAll(_activeConsumers.Keys.Select(id => StopConsumerAsync(id, CancellationToken.None)));

            _activeConsumers.Clear();
            _disposed = true;
        }
        finally {
            _disposeSemaphore.Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Registra e inicia um consumer na fila informada, fiando o despacho de cada mensagem ao
    ///     <paramref name="handler" /> com a política de retry. Cria uma tarefa de monitor que mantém o
    ///     consumer rastreado para verificação de saúde e parada, e retorna o id que o identifica.
    /// </summary>
    /// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
    /// <returns>Identificador do consumer recém-iniciado, usado nas demais operações.</returns>
    public async Task<string> StartConsumerAsync<T>(IChannel channel, string queue, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        retryOptions ??= ConsumerRetryOptions.None();

        var consumerId = Guid.NewGuid().ToString();
        var consumerTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, eventArgs) => ProcessMessageAsync(consumerId, channel, queue, exchange,
            routingKey, retryOptions, eventArgs, handler, consumerTokenSource.Token);

        var consumerTag = await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken);

        var consumerInfo = new ConsumerInfo {
            ConsumerId = consumerId,
            ConsumerTag = consumerTag,
            Channel = channel,
            Consumer = consumer,
            Queue = queue,
            Exchange = exchange,
            RoutingKey = routingKey,
            StartedAt = DateTimeOffset.UtcNow,
            CancellationTokenSource = consumerTokenSource
        };
        _activeConsumers.TryAdd(consumerId, consumerInfo);

        consumerInfo.ConsumerTask = Task.Run(async () => {
            try {
                while (!consumerTokenSource.Token.IsCancellationRequested)
                    await Task.Delay(1000, consumerTokenSource.Token);
            }
            catch (OperationCanceledException) {
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Erro fatal no monitor do consumer {ConsumerId}.", consumerId);
                consumerInfo.IsDisposed = true;
            }
        }, consumerTokenSource.Token);

        _logger.LogDebug("Consumer {ConsumerId} iniciado para a fila {Queue}.", consumerId, queue);
        await Task.Delay(100, cancellationToken);
        return consumerId;
    }

    /// <summary>
    ///     Para o consumer identificado: cancela seu token, aguarda o monitor encerrar e cancela a assinatura no
    ///     canal. Não faz nada se o id não estiver mais ativo. Não propaga exceções (apenas registra).
    /// </summary>
    public async Task StopConsumerAsync(string consumerId, CancellationToken cancellationToken = default) {
        if (!_activeConsumers.TryRemove(consumerId, out var consumerInfo)) return;

        try {
            await consumerInfo.CancellationTokenSource.CancelAsync();

            if (consumerInfo.ConsumerTask is not null)
                try {
                    await consumerInfo.ConsumerTask;
                }
                catch (OperationCanceledException) {
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Erro aguardando o monitor do consumer {ConsumerId}.", consumerId);
                }

            if (consumerInfo.Channel.IsOpen)
                await consumerInfo.Channel.BasicCancelAsync(consumerInfo.ConsumerTag, cancellationToken: cancellationToken);

            consumerInfo.CancellationTokenSource.Dispose();
            _logger.LogDebug("Consumer {ConsumerId} parado.", consumerId);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Erro ao parar o consumer {ConsumerId}.", consumerId);
        }
    }

    /// <summary>
    ///     Indica se o consumer está saudável: ativo, não descartado, com a tarefa de monitor viva e o canal
    ///     aberto. Retorna <c>false</c> para id desconhecido ou diante de qualquer falha na verificação.
    /// </summary>
    public Task<bool> IsConsumerHealthyAsync(string consumerId) {
        if (!_activeConsumers.TryGetValue(consumerId, out var info))
            return Task.FromResult(false);

        try {
            var healthy = !info.CancellationTokenSource.Token.IsCancellationRequested &&
                          !info.IsDisposed &&
                          info.ConsumerTask is { IsCompleted: false, IsFaulted: false, IsCanceled: false } &&
                          info.Channel.IsOpen;
            return Task.FromResult(healthy);
        }
        catch {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Varre os consumers ativos e para os que estiverem não saudáveis, liberando seus recursos para que a
    ///     camada superior possa reassiná-los. A reassinatura em si fica a cargo de quem chama.
    /// </summary>
    public async Task RestartUnhealthyConsumersAsync(CancellationToken cancellationToken = default) {
        var unhealthy = new List<string>();
        foreach (var id in _activeConsumers.Keys)
            if (!await IsConsumerHealthyAsync(id))
                unhealthy.Add(id);

        foreach (var id in unhealthy) {
            _logger.LogWarning("Reiniciando consumer não saudável {ConsumerId}.", id);
            await StopConsumerAsync(id, cancellationToken);
        }
    }

    /// <summary>
    ///     Núcleo de consumo de uma mensagem: materializa o payload, invoca o handler e decide o destino conforme
    ///     o resultado. O tipo da exceção define a ação — <see cref="SkippableMessageException" /> descarta com
    ///     ACK; <see cref="PermanentFailureException" /> vai à DLQ; <see cref="RetryableException" /> e
    ///     <see cref="ExternalServiceException" /> escolhem health-retry, backoff ou requeue imediato segundo a
    ///     política; e exceções inesperadas caem no backoff (ou requeue, se desabilitado). Mensagens que excedem
    ///     <see cref="ConsumerRetryOptions.MaxRetries" /> são enviadas direto à DLQ.
    /// </summary>
    private async Task ProcessMessageAsync<T>(string consumerId, IChannel channel, string queue, string exchange,
        string routingKey, ConsumerRetryOptions retryOptions, BasicDeliverEventArgs eventArgs,
        IMessageHandler<T> handler, CancellationToken cancellationToken) where T : class, IMessage {
        try {
            if (eventArgs.Body.IsEmpty) {
                _logger.LogWarning("Mensagem vazia recebida no consumer {ConsumerId}.", consumerId);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
                return;
            }

            T? message;
            try {
                message = Materialize<T>(eventArgs);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Falha ao desserializar mensagem no consumer {ConsumerId}.", consumerId);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
                return;
            }

            if (message is null) {
                _logger.LogWarning("Mensagem desserializada é nula no consumer {ConsumerId}.", consumerId);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, false, cancellationToken);
                return;
            }

            var deathCount = GetDeathCount(eventArgs.BasicProperties);

            if (retryOptions.EnableDeadLetter && deathCount >= retryOptions.MaxRetries) {
                _logger.LogError("Mensagem excedeu o limite de tentativas ({MaxRetries}, death={DeathCount}). Enviando à DLQ.",
                    retryOptions.MaxRetries, deathCount);
                await SendToFinalDeadLetterQueueAsync(channel, queue, eventArgs, message.CorrelationId, deathCount,
                    cancellationToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
                return;
            }

            using var activity = new Activity("RabbitMQ.Consumer");
            if (!string.IsNullOrEmpty(message.CorrelationId)) activity.SetParentId(message.CorrelationId);
            activity.Start();

            await handler.HandleAsync(message, cancellationToken);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (OperationCanceledException) {
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, CancellationToken.None);
        }
        catch (SkippableMessageException ex) {
            _logger.LogDebug(ex, "Mensagem descartada por {ExceptionType}: {Reason}.", ex.GetType().Name, ex.Reason);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (PermanentFailureException ex) {
            if (retryOptions.EnableDeadLetter && ex.SendToDeadLetterQueue) {
                _logger.LogError(ex, "Falha permanente ({ExceptionType}). Enviando à DLQ.", ex.GetType().Name);
                await SendToFinalDeadLetterQueueAsync(channel, queue, eventArgs,
                    eventArgs.BasicProperties.CorrelationId ?? string.Empty, GetDeathCount(eventArgs.BasicProperties),
                    cancellationToken);
            }
            else {
                _logger.LogError(ex, "Falha permanente ({ExceptionType}). DLQ desabilitada — descartando com ACK.",
                    ex.GetType().Name);
            }

            await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
        }
        catch (RetryableException ex) {
            var reason = ex switch {
                MessageBeingProcessedException mb => $"being-processed:{mb.ProcessingInstance}",
                ServiceTemporarilyUnavailableException st => $"service-unavailable:{st.ServiceName}",
                MessageProcessingTimeoutException mt => $"timeout:{mt.Operation}",
                _ => "retryable-error"
            };
            var isBeingProcessed = ex is MessageBeingProcessedException;

            if (isBeingProcessed || !retryOptions.EnableHealthRetry) {
                _logger.Log(isBeingProcessed ? LogLevel.Debug : LogLevel.Warning, ex,
                    "Erro retryable ({ExceptionType}). Requeue imediato.", ex.GetType().Name);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, cancellationToken);
            }
            else {
                _logger.LogWarning(ex, "Erro retryable ({ExceptionType}). Aguardará {Delay}s na health-retry.",
                    ex.GetType().Name, retryOptions.HealthCheckRetryDelaySeconds);
                await SendToHealthCheckRetryQueueAsync(channel, queue, exchange, routingKey, eventArgs, retryOptions,
                    reason, cancellationToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
            }
        }
        catch (ExternalServiceException ex) {
            if (ex.ShouldRequeue && retryOptions.EnableHealthRetry) {
                _logger.LogWarning(ex, "Serviço externo {ServiceName} transitório ({StatusCode}). Health-retry.",
                    ex.ServiceName, ex.StatusCode);
                await SendToHealthCheckRetryQueueAsync(channel, queue, exchange, routingKey, eventArgs, retryOptions,
                    $"external-api-{ex.StatusCode}", cancellationToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
            }
            else if (!ex.ShouldRequeue && retryOptions.EnableRetryWithBackoff) {
                _logger.LogError(ex, "Serviço externo {ServiceName} ({StatusCode}). Backoff-retry.",
                    ex.ServiceName, ex.StatusCode);
                await SendToBackoffRetryQueueAsync(channel, queue, exchange, routingKey, eventArgs, retryOptions,
                    $"external-api-{ex.StatusCode}", cancellationToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
            }
            else {
                _logger.LogWarning(ex, "Serviço externo {ServiceName} ({StatusCode}). Requeue imediato.",
                    ex.ServiceName, ex.StatusCode);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, cancellationToken);
            }
        }
        catch (Exception ex) {
            if (retryOptions.EnableRetryWithBackoff) {
                _logger.LogError(ex, "Erro inesperado no consumer {ConsumerId}. Backoff-retry.", consumerId);
                await SendToBackoffRetryQueueAsync(channel, queue, exchange, routingKey, eventArgs, retryOptions,
                    "unexpected-error", cancellationToken);
                await channel.BasicAckAsync(eventArgs.DeliveryTag, false, cancellationToken);
            }
            else {
                _logger.LogError(ex, "Erro inesperado no consumer {ConsumerId}. Requeue imediato.", consumerId);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Materializa a mensagem do corpo recebido: para tipos <see cref="IRawMessage" />, instancia e chama
    ///     <see cref="IRawMessage.FromRaw" /> com os bytes brutos; caso contrário, desserializa pelo
    ///     content-type informado nas propriedades.
    /// </summary>
    private T? Materialize<T>(BasicDeliverEventArgs eventArgs) where T : class, IMessage {
        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            var raw = (IRawMessage)Activator.CreateInstance(typeof(T))!;
            raw.FromRaw(eventArgs.Body.ToArray(), eventArgs.BasicProperties.CorrelationId ?? string.Empty);
            return (T)raw;
        }

        var serializer = _serializers.ResolveForContentType(eventArgs.BasicProperties.ContentType);
        return serializer.Deserialize(eventArgs.Body, typeof(T)) as T;
    }

    private static int GetDeathCount(IReadOnlyBasicProperties? properties) {
        if (properties?.Headers is null) return 0;

        if (properties.Headers.TryGetValue("x-death-count", out var custom))
            return custom switch {
                long l => (int)l,
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 0
            };

        return 0;
    }

    private async Task SendToHealthCheckRetryQueueAsync(IChannel channel, string queue, string exchange,
        string routingKey, BasicDeliverEventArgs eventArgs, ConsumerRetryOptions retryOptions, string reason,
        CancellationToken cancellationToken) {
        await RepublishToDelayQueueAsync(channel, $"{queue}.health-retry", exchange, routingKey,
            retryOptions.HealthCheckRetryDelaySeconds * 1000, eventArgs, reason, false, cancellationToken);
    }

    private async Task SendToBackoffRetryQueueAsync(IChannel channel, string queue, string exchange, string routingKey,
        BasicDeliverEventArgs eventArgs, ConsumerRetryOptions retryOptions, string reason,
        CancellationToken cancellationToken) {
        await RepublishToDelayQueueAsync(channel, $"{queue}.backoff-retry", exchange, routingKey,
            retryOptions.BackoffDelaySeconds * 1000, eventArgs, reason, true, cancellationToken);
    }

    private async Task RepublishToDelayQueueAsync(IChannel channel, string retryQueueName, string exchange,
        string routingKey, int ttlMs, BasicDeliverEventArgs eventArgs, string reason, bool incrementDeathCount,
        CancellationToken cancellationToken) {
        try {
            await channel.ExchangeDeclareAsync(exchange.ToLowerInvariant(), ExchangeType.Topic, true,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(retryQueueName, true, false, false, new Dictionary<string, object?> {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = ttlMs,
                ["x-dead-letter-exchange"] = exchange.ToLowerInvariant(),
                ["x-dead-letter-routing-key"] = routingKey
            }, cancellationToken: cancellationToken);

            var headers = eventArgs.BasicProperties.Headers is not null
                ? new Dictionary<string, object?>(eventArgs.BasicProperties.Headers)
                : new Dictionary<string, object?>();
            headers["x-retry-reason"] = reason;
            if (incrementDeathCount) headers["x-death-count"] = GetDeathCount(eventArgs.BasicProperties) + 1;

            var props = new BasicProperties {
                Persistent = true,
                ContentType = eventArgs.BasicProperties.ContentType ?? "application/json",
                CorrelationId = eventArgs.BasicProperties.CorrelationId,
                Headers = headers
            };

            await channel.BasicPublishAsync(string.Empty, retryQueueName, false, props, eventArgs.Body, cancellationToken);
            _logger.LogDebug("Mensagem enviada à fila de retry {RetryQueue} (aguardará {Ttl}ms). Motivo: {Reason}.",
                retryQueueName, ttlMs, reason);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao enviar à fila de retry {RetryQueue}. Requeue imediato.", retryQueueName);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, true, cancellationToken);
        }
    }

    /// <summary>
    ///     Encaminha a mensagem à DLQ final (<c>{fila}.dlq</c> no exchange <c>default.dlq</c>), anexando headers
    ///     de diagnóstico (fila/exchange originais, contagem de mortes, instante e correlação). Republica o corpo
    ///     original sem reserializá-lo, preservando o payload exatamente como recebido.
    /// </summary>
    private async Task SendToFinalDeadLetterQueueAsync(IChannel channel, string queue,
        BasicDeliverEventArgs eventArgs, string correlationId, int deathCount, CancellationToken cancellationToken) {
        const string dlqExchange = "default.dlq";
        var dlqName = $"{queue}.dlq";

        var headers = new Dictionary<string, object?> {
            ["x-original-queue"] = queue,
            ["x-original-exchange"] = eventArgs.Exchange,
            ["x-death-count"] = deathCount,
            ["x-failed-at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["x-correlation-id"] = correlationId
        };

        var props = new BasicProperties {
            Persistent = true,
            ContentType = eventArgs.BasicProperties.ContentType ?? "application/json",
            CorrelationId = correlationId,
            Headers = headers
        };

        await channel.BasicPublishAsync(dlqExchange, dlqName, false, props, eventArgs.Body, cancellationToken);
        _logger.LogWarning("Mensagem enviada à DLQ final {DlqName} (correlation {CorrelationId}, death {DeathCount}).",
            dlqName, correlationId, deathCount);
    }

    private sealed class ConsumerInfo
    {
        public string ConsumerId { get; init; } = string.Empty;
        public string ConsumerTag { get; init; } = string.Empty;
        public IChannel Channel { get; init; } = null!;
        public AsyncEventingBasicConsumer Consumer { get; init; } = null!;
        public string Queue { get; init; } = string.Empty;
        public string Exchange { get; init; } = string.Empty;
        public string RoutingKey { get; init; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; }
        public CancellationTokenSource CancellationTokenSource { get; init; } = null!;
        public Task? ConsumerTask { get; set; }
        public bool IsDisposed { get; set; }
    }
}
