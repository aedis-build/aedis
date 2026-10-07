using System.Collections.Concurrent;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.Telemetry;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Consumers SQS com long-polling e um <strong>pump de concorrência limitada</strong>: no máximo
///     <see cref="AwsSqsOptions.MaxNumberOfMessages" /> mensagens em voo por fila, cada ReceiveMessage pede só o
///     que cabe nos slots livres e um slot é reposto assim que a mensagem conclui — uma mensagem lenta não
///     segura o lote nem empurra as demais para além do visibility timeout. Um supervisor por fila religa o
///     loop se ele morrer. O destino da mensagem segue a taxonomia de desfecho: sucesso e descartes
///     (<c>Skipped</c>/<c>Duplicate</c>) dão ACK (DeleteMessage); falhas não apagam — a mensagem volta após o
///     visibility timeout e, ao exceder o maxReceiveCount do RedrivePolicy, o próprio SQS a move para a DLQ.
///     A política de retry é, portanto, o redrive nativo da fila; <see cref="ConsumerRetryOptions" /> não é
///     reinterpretada aqui.
/// </summary>
public sealed class AwsSqsConsumerManager(
    IAwsPubSubFactory factory,
    IOptions<AwsSqsOptions> options,
    ILogger<AwsSqsConsumerManager> logger,
    MessageSerializerResolver serializers,
    MessageEncoderResolver encoders)
{
    internal const string SystemName = "aws_sqs";

    private readonly ConcurrentDictionary<string, ConsumerState> _consumers = new();
    private readonly AwsSqsOptions _options = options.Value;

    /// <summary>
    ///     Inicia o consumer da fila (no-op se já houver um ativo para ela): resolve a URL e sobe o loop
    ///     supervisionado em background. A falha de uma mensagem nunca derruba o loop; a falha do loop é
    ///     registrada e o loop é religado após <see cref="AwsSqsOptions.ReceiveErrorBackoffMs" />.
    /// </summary>
    public async Task StartConsumerAsync<T>(string queueName, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        var name = factory.ResolveQueueName(queueName);

        if (_consumers.ContainsKey(name)) {
            logger.LogWarning("Consumer da fila '{QueueName}' já está em execução.", name);
            return;
        }

        var sqsClient = await factory.GetSqsClientAsync(cancellationToken);
        var queueUrl = (await sqsClient.GetQueueUrlAsync(name, cancellationToken)).QueueUrl;

        var state = new ConsumerState(name, queueUrl, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        if (!_consumers.TryAdd(name, state)) {
            state.Cts.Dispose();
            return;
        }

        state.Loop = Task.Run(() => SuperviseAsync(state, handler), CancellationToken.None);
        logger.LogDebug("Consumer iniciado para a fila '{QueueName}' (em voo máx. {MaxInFlight}).", name, _options.MaxNumberOfMessages);
    }

    /// <summary>Para o loop de consumo da fila (cancela e aguarda a drenagem das mensagens em voo); no-op sem consumer ativo.</summary>
    public async Task StopConsumerAsync(string queueName, CancellationToken cancellationToken = default) {
        var name = factory.ResolveQueueName(queueName);
        if (!_consumers.TryRemove(name, out var state)) return;

        state.Cts.Cancel();
        if (state.Loop is { } loop)
            await Task.WhenAny(loop, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        state.Cts.Dispose();

        logger.LogDebug("Consumer da fila '{QueueName}' parado.", name);
    }

    /// <summary>Indica se há um loop de consumo ativo (não concluído) para a fila informada.</summary>
    public Task<bool> IsConsumerHealthyAsync(string queueName, CancellationToken cancellationToken = default) {
        var healthy = _consumers.TryGetValue(factory.ResolveQueueName(queueName), out var state)
                      && !state.Cts.IsCancellationRequested
                      && state.Loop is { IsCompleted: false };
        return Task.FromResult(healthy);
    }

    private async Task SuperviseAsync<T>(ConsumerState state, IMessageHandler<T> handler) where T : class, IMessage {
        var ct = state.Cts.Token;

        try {
            while (!ct.IsCancellationRequested)
                try {
                    await ProcessLoopAsync(state, handler, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (Exception ex) {
                    logger.LogError(ex, "Loop de consumo de '{QueueName}' morreu; religando em {Delay} ms.",
                        state.QueueName, _options.ReceiveErrorBackoffMs);
                    await DelayAsync(ct);
                }
        }
        finally {
            _consumers.TryRemove(new KeyValuePair<string, ConsumerState>(state.QueueName, state));
            logger.LogDebug("Supervisor do consumer '{QueueName}' encerrado.", state.QueueName);
        }
    }

    private async Task ProcessLoopAsync<T>(ConsumerState state, IMessageHandler<T> handler, CancellationToken ct)
        where T : class, IMessage {
        var sqsClient = await factory.GetSqsClientAsync(ct);
        var maxInFlight = Math.Max(1, _options.MaxNumberOfMessages);
        var inFlight = new HashSet<Task>();

        try {
            while (!ct.IsCancellationRequested)
                try {
                    if (inFlight.Count >= maxInFlight)
                        inFlight.Remove(await Task.WhenAny(inFlight));

                    inFlight.RemoveWhere(task => task.IsCompleted);

                    var freeSlots = maxInFlight - inFlight.Count;
                    if (freeSlots <= 0) continue;

                    var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest {
                        QueueUrl = state.QueueUrl,
                        MaxNumberOfMessages = freeSlots,
                        WaitTimeSeconds = _options.WaitTimeSeconds,
                        MessageAttributeNames = ["All"],
                        MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                    }, ct);

                    if (response.Messages is null or { Count: 0 })
                        continue;

                    foreach (var message in response.Messages)
                        inFlight.Add(ProcessOneAsync(message, state, handler, sqsClient, ct));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (Exception ex) {
                    logger.LogError(ex, "Erro no loop de consumo de '{QueueName}'; nova tentativa em {Delay} ms.",
                        state.QueueName, _options.ReceiveErrorBackoffMs);
                    await DelayAsync(ct);
                }
        }
        finally {
            await DrainAsync(inFlight);
        }
    }

    private async Task ProcessOneAsync<T>(Message sqsMessage, ConsumerState state, IMessageHandler<T> handler,
        IAmazonSQS sqsClient, CancellationToken ct) where T : class, IMessage {
        var payload = ExtractPayload(sqsMessage);
        var receiveCount = ReadReceiveCount(sqsMessage);

        using var operation = MessagingInstrumentation.StartProcess(SystemName, state.QueueName, payload.GetTraceHeader,
            payload.CorrelationId, sqsMessage.MessageId, receiveCount, logger);

        var outcome = MessageOutcome.Success;
        try {
            var message = Deserialize<T>(payload)
                          ?? throw new InvalidOperationException("Falha ao desserializar a mensagem SQS.");
            operation.SetMessage(message);

            await handler.HandleAsync(message, ct);
        }
        catch (Exception ex) {
            outcome = MessageOutcomeClassifier.FromException(ex);
            operation.Complete(outcome, ex);

            if (MessageOutcomeClassifier.ShouldAcknowledge(outcome))
                logger.LogDebug(ex, "Mensagem {MessageId} descartada ({Outcome}); será confirmada.", sqsMessage.MessageId, outcome);
            else
                logger.LogWarning(ex, "Falha ao processar a mensagem {MessageId} (tentativa {ReceiveCount}); voltará após o visibility timeout.",
                    sqsMessage.MessageId, receiveCount);
        }

        if (!MessageOutcomeClassifier.ShouldAcknowledge(outcome)) return;

        try {
            await sqsClient.DeleteMessageAsync(state.QueueUrl, sqsMessage.ReceiptHandle, ct);
            operation.Complete(MessageOutcome.Success);
            logger.LogDebug("Mensagem {MessageId} confirmada e removida da fila.", sqsMessage.MessageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Mensagem {MessageId} processada, mas o DeleteMessage falhou; será reentregue.", sqsMessage.MessageId);
        }
    }

    private T? Deserialize<T>(PayloadContext payload) where T : class, IMessage {
        var bytes = AwsMessageTransportCodec.Decode(payload.RawMessage, payload.ContentTransferEncoding,
            payload.ContentEncoding, encoders);

        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            var instance = Activator.CreateInstance<T>();
            ((IRawMessage)instance).FromRaw(bytes, payload.CorrelationId ?? string.Empty);
            return instance;
        }

        return serializers.ResolveForContentType(payload.ContentType).Deserialize(bytes, typeof(T)) as T;
    }

    private static PayloadContext ExtractPayload(Message sqsMessage) {
        var contentType = ReadAttribute(sqsMessage, "Content-Type", "ContentType");
        var contentEncoding = ReadAttribute(sqsMessage, "Content-Encoding", "ContentEncoding");
        var transferEncoding = ReadAttribute(sqsMessage, "Content-Transfer-Encoding", "ContentTransferEncoding");
        var correlationId = ReadAttribute(sqsMessage, "CorrelationId");
        var traceParent = ReadAttribute(sqsMessage, MessagingInstrumentation.TraceParentHeader);
        var traceState = ReadAttribute(sqsMessage, MessagingInstrumentation.TraceStateHeader);

        if (sqsMessage.Body is { } body && AwsPubSubEnvelopeParser.IsSnsEnvelope(body)) {
            var envelope = AwsPubSubEnvelopeParser.Parse(body);
            return new PayloadContext(envelope.Message,
                envelope.ContentType ?? contentType,
                envelope.ContentEncoding ?? contentEncoding,
                envelope.ContentTransferEncoding ?? transferEncoding,
                envelope.CorrelationId ?? correlationId,
                envelope.TraceParent ?? traceParent,
                envelope.TraceState ?? traceState);
        }

        return new PayloadContext(sqsMessage.Body ?? string.Empty, contentType, contentEncoding, transferEncoding,
            correlationId, traceParent, traceState);
    }

    private static int ReadReceiveCount(Message sqsMessage) =>
        sqsMessage.Attributes is not null
        && sqsMessage.Attributes.TryGetValue("ApproximateReceiveCount", out var raw)
        && int.TryParse(raw, out var count)
            ? count
            : 1;

    private static string? ReadAttribute(Message sqsMessage, params string[] names) {
        if (sqsMessage.MessageAttributes is null) return null;

        foreach (var name in names)
            if (sqsMessage.MessageAttributes.TryGetValue(name, out var value))
                return value.StringValue;

        return null;
    }

    private async Task DelayAsync(CancellationToken ct) {
        try {
            await Task.Delay(_options.ReceiveErrorBackoffMs, ct);
        }
        catch (OperationCanceledException) {
        }
    }

    private static async Task DrainAsync(HashSet<Task> inFlight) {
        try {
            await Task.WhenAll(inFlight);
        }
        catch {
        }
    }

    private sealed class ConsumerState(string queueName, string queueUrl, CancellationTokenSource cts)
    {
        public string QueueName { get; } = queueName;
        public string QueueUrl { get; } = queueUrl;
        public CancellationTokenSource Cts { get; } = cts;
        public Task? Loop { get; set; }
    }

    private sealed record PayloadContext(
        string RawMessage,
        string? ContentType,
        string? ContentEncoding,
        string? ContentTransferEncoding,
        string? CorrelationId,
        string? TraceParent,
        string? TraceState)
    {
        public string? GetTraceHeader(string name) => name switch {
            MessagingInstrumentation.TraceParentHeader => TraceParent,
            MessagingInstrumentation.TraceStateHeader => TraceState,
            _ => null
        };
    }
}
