using System.Collections.Concurrent;
using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Gerencia os consumidores do Azure Service Bus via <see cref="ServiceBusProcessor" /> (PeekLock,
///     complete/abandon/dead-letter manuais). Aplica a taxonomia de exceções do Aedis para decidir o
///     destino de cada mensagem: descarte (skippable), DLQ (permanente), reentrega imediata ou retry
///     agendado (<see cref="ServiceBusSender.ScheduleMessageAsync" />). Desserializa via
///     <see cref="MessageSerializerResolver" />.
/// </summary>
internal sealed class ServiceBusConsumerManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConsumerInfo> _consumers = new();
    private readonly SemaphoreSlim _disposeSemaphore = new(1, 1);
    private readonly ILogger _logger;
    private readonly ServiceBusOptions _options;
    private readonly MessageSerializerResolver _serializers;
    private ServiceBusClient? _client;
    private bool _disposed;

    public ServiceBusConsumerManager(ILogger logger, IOptions<ServiceBusOptions> options,
        MessageSerializerResolver serializers) {
        _logger = logger;
        _options = options.Value;
        _serializers = serializers;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        await _disposeSemaphore.WaitAsync();
        try {
            if (_disposed) return;
            await Task.WhenAll(_consumers.Keys.Select(id => StopConsumerAsync(id, CancellationToken.None)));
            _consumers.Clear();
            _disposed = true;
        }
        finally {
            _disposeSemaphore.Release();
        }
    }

    public async Task<string> StartConsumerAsync<T>(ServiceBusClient client, string queue, string exchange,
        string routingKey, IMessageHandler<T> handler, ConsumerRetryOptions retryOptions,
        CancellationToken cancellationToken = default) where T : class, IMessage {
        _client = client;
        var consumerId = Guid.NewGuid().ToString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var processorOptions = new ServiceBusProcessorOptions {
            MaxConcurrentCalls = _options.MaxConcurrentCalls,
            AutoCompleteMessages = false,
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        };

        var processor = ServiceBusBaseService.IsTopic(exchange)
            ? client.CreateProcessor(ServiceBusBaseService.NormalizeName(exchange),
                ServiceBusBaseService.NormalizeName(queue), processorOptions)
            : client.CreateProcessor(ServiceBusBaseService.NormalizeName(queue), processorOptions);

        processor.ProcessMessageAsync += args => ProcessMessageAsync(consumerId, processor, retryOptions, args,
            handler, cts.Token);
        processor.ProcessErrorAsync += args => {
            _logger.LogError(args.Exception, "Erro no processor do consumer {ConsumerId}: {Source}.",
                consumerId, args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(cancellationToken);

        _consumers.TryAdd(consumerId, new ConsumerInfo { Processor = processor, Cts = cts });
        _logger.LogDebug("Consumer Azure Service Bus {ConsumerId} iniciado na fila/subscription {Queue}.",
            consumerId, queue);

        await Task.Delay(50, cancellationToken);
        return consumerId;
    }

    public Task<bool> IsConsumerHealthyAsync(string consumerId) {
        if (!_consumers.TryGetValue(consumerId, out var info))
            return Task.FromResult(false);

        var healthy = !info.Cts.Token.IsCancellationRequested && info.Processor is { IsProcessing: true };
        return Task.FromResult(healthy);
    }

    public async Task RestartUnhealthyConsumersAsync(CancellationToken cancellationToken = default) {
        foreach (var id in _consumers.Keys)
            if (!await IsConsumerHealthyAsync(id)) {
                _logger.LogWarning("Reiniciando o consumer Azure Service Bus não saudável {ConsumerId}.", id);
                await StopConsumerAsync(id, cancellationToken);
            }
    }

    public async Task StopConsumerAsync(string consumerId, CancellationToken cancellationToken = default) {
        if (!_consumers.TryRemove(consumerId, out var info))
            return;

        try {
            await info.Cts.CancelAsync();
            if (info.Processor != null) {
                await info.Processor.StopProcessingAsync(cancellationToken);
                await info.Processor.DisposeAsync();
            }

            info.Cts.Dispose();
            _logger.LogDebug("Consumer Azure Service Bus {ConsumerId} parado.", consumerId);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Erro ao parar o consumer {ConsumerId}.", consumerId);
        }
    }

    private async Task ProcessMessageAsync<T>(string consumerId, ServiceBusProcessor processor,
        ConsumerRetryOptions retryOptions, ProcessMessageEventArgs args, IMessageHandler<T> handler,
        CancellationToken cancellationToken) where T : class, IMessage {
        try {
            if (args.Message.Body.ToMemory().IsEmpty) {
                _logger.LogWarning("Mensagem vazia no consumer {ConsumerId}.", consumerId);
                await args.CompleteMessageAsync(args.Message, cancellationToken);
                return;
            }

            var message = Deserialize<T>(args.Message);
            if (message is null) {
                _logger.LogWarning("Falha ao desserializar a mensagem no consumer {ConsumerId} — para a DLQ.",
                    consumerId);
                await args.DeadLetterMessageAsync(args.Message, cancellationToken: cancellationToken);
                return;
            }

            var deliveryCount = GetDeliveryCount(args.Message);
            if (retryOptions.EnableDeadLetter && deliveryCount >= retryOptions.MaxRetries) {
                _logger.LogWarning("Mensagem excedeu {MaxRetries} tentativas (delivery {DeliveryCount}) — para a DLQ.",
                    retryOptions.MaxRetries, deliveryCount);
                await args.DeadLetterMessageAsync(args.Message, cancellationToken: cancellationToken);
                return;
            }

            await handler.HandleAsync(message, cancellationToken);
            await args.CompleteMessageAsync(args.Message, cancellationToken);
        }
        catch (OperationCanceledException) {
            await args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
        }
        catch (SkippableMessageException ex) {
            _logger.LogDebug(ex, "Mensagem descartada por {Type}. Motivo: {Reason}.", ex.GetType().Name, ex.Reason);
            await args.CompleteMessageAsync(args.Message, cancellationToken);
        }
        catch (PermanentFailureException ex) {
            if (retryOptions.EnableDeadLetter && ex.SendToDeadLetterQueue) {
                _logger.LogError(ex, "Erro permanente ({Type}) — mensagem para a DLQ.", ex.GetType().Name);
                await args.DeadLetterMessageAsync(args.Message, cancellationToken: cancellationToken);
            }
            else {
                _logger.LogError(ex, "Erro permanente ({Type}) — DLQ desabilitada, descartando.", ex.GetType().Name);
                await args.CompleteMessageAsync(args.Message, cancellationToken);
            }
        }
        catch (RetryableException ex) {
            var isBeingProcessed = ex is MessageBeingProcessedException;
            if (isBeingProcessed || !retryOptions.EnableHealthRetry) {
                await args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
            }
            else {
                await ScheduleRetryAsync(processor, args, retryOptions, true, cancellationToken);
                await args.CompleteMessageAsync(args.Message, cancellationToken);
            }
        }
        catch (ExternalServiceException ex) {
            var useHealthRetry = ex.ShouldRequeue && retryOptions.EnableHealthRetry;
            var useBackoff = !ex.ShouldRequeue && retryOptions.EnableRetryWithBackoff;

            if (useHealthRetry || useBackoff) {
                await ScheduleRetryAsync(processor, args, retryOptions, useHealthRetry, cancellationToken);
                await args.CompleteMessageAsync(args.Message, cancellationToken);
            }
            else {
                await args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex) {
            if (retryOptions.EnableRetryWithBackoff) {
                _logger.LogError(ex, "Erro inesperado no consumer {ConsumerId} — retry com backoff.", consumerId);
                await ScheduleRetryAsync(processor, args, retryOptions, false, cancellationToken);
                await args.CompleteMessageAsync(args.Message, cancellationToken);
            }
            else {
                _logger.LogError(ex, "Erro inesperado no consumer {ConsumerId} — reentrega imediata.", consumerId);
                await args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>Reagenda a mensagem na própria entidade com um atraso (retry de saúde ou backoff).</summary>
    private async Task ScheduleRetryAsync(ServiceBusProcessor processor, ProcessMessageEventArgs args,
        ConsumerRetryOptions retryOptions, bool isHealthRetry, CancellationToken cancellationToken) {
        try {
            if (_client is null)
                throw new InvalidOperationException("ServiceBusClient indisponível.");

            var delay = TimeSpan.FromSeconds(isHealthRetry
                ? retryOptions.HealthCheckRetryDelaySeconds
                : retryOptions.BackoffDelaySeconds);

            var entity = processor.EntityPath.Contains('/')
                ? processor.EntityPath.Split('/')[0]
                : processor.EntityPath;

            await using var sender = _client.CreateSender(entity);

            var resend = new ServiceBusMessage(args.Message.Body) {
                ContentType = args.Message.ContentType,
                CorrelationId = args.Message.CorrelationId,
                MessageId = args.Message.MessageId,
                Subject = args.Message.Subject
            };
            foreach (var prop in args.Message.ApplicationProperties)
                resend.ApplicationProperties[prop.Key] = prop.Value;

            resend.ApplicationProperties["x-delivery-count"] = GetDeliveryCount(args.Message) + 1;
            resend.ApplicationProperties["x-retry-type"] = isHealthRetry ? "health" : "backoff";

            await sender.ScheduleMessageAsync(resend, DateTimeOffset.UtcNow.Add(delay), cancellationToken);
            _logger.LogDebug("Mensagem reagendada para retry em {Delay}s ({Type}).",
                delay.TotalSeconds, isHealthRetry ? "health" : "backoff");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao reagendar a mensagem — reentrega imediata.");
            await args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
        }
    }

    private T? Deserialize<T>(ServiceBusReceivedMessage message) where T : class, IMessage {
        var bytes = message.Body.ToArray();

        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            var instance = Activator.CreateInstance<T>();
            ((IRawMessage)instance).FromRaw(bytes, message.CorrelationId ?? string.Empty);
            return instance;
        }

        var serializer = _serializers.ResolveForContentType(message.ContentType);
        return serializer.Deserialize(bytes, typeof(T)) as T;
    }

    private static int GetDeliveryCount(ServiceBusReceivedMessage message) {
        if (message.ApplicationProperties.TryGetValue("x-delivery-count", out var value))
            return value switch {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => message.DeliveryCount
            };

        return message.DeliveryCount;
    }

    private sealed class ConsumerInfo
    {
        public ServiceBusProcessor? Processor { get; init; }
        public CancellationTokenSource Cts { get; init; } = null!;
    }
}
