using System.Collections.Concurrent;
using System.Text;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Consumers SQS com long-polling: um loop por fila que recebe lotes, processa em paralelo e dá ACK
///     (DeleteMessage) no sucesso. Em falha, a mensagem não é apagada e volta após o visibility timeout;
///     ao exceder o maxReceiveCount do RedrivePolicy, o próprio SQS a move para a DLQ.
/// </summary>
public sealed class AwsSqsConsumerManager(
    IAwsPubSubFactory factory,
    IOptions<AwsSqsOptions> options,
    ILogger<AwsSqsConsumerManager> logger,
    MessageSerializerResolver serializers,
    MessageEncoderResolver encoders)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _consumers = new();
    private readonly AwsSqsOptions _options = options.Value;

    /// <summary>
    ///     Inicia um loop de consumo em background para a fila informada (no-op se já houver um ativo para
    ///     ela). Cada lote recebido é processado em paralelo; a falha de uma mensagem não derruba o loop.
    /// </summary>
    public async Task StartConsumerAsync<T>(string queueName, string exchange, string routingKey,
        IMessageHandler<T> handler, ConsumerRetryOptions retryOptions, CancellationToken cancellationToken = default)
        where T : class, IMessage {
        var name = factory.NormalizeName(queueName);

        if (_consumers.ContainsKey(name)) {
            logger.LogWarning("Consumer da fila '{QueueName}' já está em execução.", name);
            return;
        }

        var sqsClient = await factory.GetSqsClientAsync(cancellationToken);
        var queueUrl = (await sqsClient.GetQueueUrlAsync(name, cancellationToken)).QueueUrl;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _consumers[name] = cts;

        _ = Task.Run(async () => {
            try {
                await ProcessLoopAsync(queueUrl, handler, cts.Token);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Erro fatal no loop do consumer da fila '{QueueName}'.", name);
            }
            finally {
                _consumers.TryRemove(name, out _);
            }
        }, cts.Token);

        logger.LogDebug("Consumer iniciado para a fila '{QueueName}'.", name);
    }

    /// <summary>Para o loop de consumo da fila (cancela e descarta o token); no-op se não houver consumer ativo.</summary>
    public Task StopConsumerAsync(string queueName, CancellationToken cancellationToken = default) {
        var name = factory.NormalizeName(queueName);
        if (_consumers.TryRemove(name, out var cts)) {
            cts.Cancel();
            cts.Dispose();
            logger.LogDebug("Consumer da fila '{QueueName}' parado.", name);
        }

        return Task.CompletedTask;
    }

    /// <summary>Indica se há um loop de consumo ativo para a fila informada.</summary>
    public Task<bool> IsConsumerHealthyAsync(string queueName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_consumers.ContainsKey(factory.NormalizeName(queueName)));

    private async Task ProcessLoopAsync<T>(string queueUrl, IMessageHandler<T> handler, CancellationToken ct)
        where T : class, IMessage {
        var sqsClient = await factory.GetSqsClientAsync(ct);
        logger.LogDebug("Loop de consumo iniciado para '{QueueUrl}' (long-polling {Wait}s).",
            queueUrl, _options.WaitTimeSeconds);

        while (!ct.IsCancellationRequested)
            try {
                var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = _options.MaxNumberOfMessages,
                    WaitTimeSeconds = _options.WaitTimeSeconds,
                    MessageAttributeNames = ["All"],
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                }, ct);

                if (response.Messages is null or { Count: 0 })
                    continue;

                var parallelOptions = new ParallelOptions {
                    MaxDegreeOfParallelism = _options.MaxNumberOfMessages,
                    CancellationToken = ct
                };

                await Parallel.ForEachAsync(response.Messages, parallelOptions,
                    async (message, token) => await ProcessOneAsync(message, queueUrl, handler, sqsClient, token));
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Erro no loop de consumo de '{QueueUrl}'.", queueUrl);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

        logger.LogDebug("Loop de consumo encerrado para '{QueueUrl}'.", queueUrl);
    }

    /// <summary>
    ///     Processa uma mensagem e dá ACK (DeleteMessage) só no sucesso. Em falha de desserialização ou de
    ///     handler, a mensagem deliberadamente não é apagada: ela reaparece após o visibility timeout e, ao
    ///     exceder o maxReceiveCount do RedrivePolicy, o próprio SQS a move para a DLQ.
    /// </summary>
    private async Task ProcessOneAsync<T>(Message sqsMessage, string queueUrl, IMessageHandler<T> handler,
        IAmazonSQS sqsClient, CancellationToken ct) where T : class, IMessage {
        try {
            var message = Deserialize<T>(sqsMessage);
            if (message is null) {
                logger.LogWarning("Falha ao desserializar a mensagem {MessageId} — será reentregue.",
                    sqsMessage.MessageId);
                return;
            }

            await handler.HandleAsync(message, ct);
            await sqsClient.DeleteMessageAsync(queueUrl, sqsMessage.ReceiptHandle, ct);
            logger.LogDebug("Mensagem {MessageId} processada e removida da fila.", sqsMessage.MessageId);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "Falha ao processar a mensagem {MessageId} — será reentregue.",
                sqsMessage.MessageId);
        }
    }

    private T? Deserialize<T>(Message sqsMessage) where T : class, IMessage {
        var (rawMessage, contentType, contentEncoding) = ExtractPayload(sqsMessage);
        var transported = AwsPubSubEnvelopeParser.TryFromBase64(rawMessage) ?? Encoding.UTF8.GetBytes(rawMessage);
        var encoder = encoders.ResolveForContentEncoding(contentEncoding);
        var bytes = encoder.Decode(transported).ToArray();

        if (typeof(IRawMessage).IsAssignableFrom(typeof(T))) {
            var instance = Activator.CreateInstance<T>();
            var correlationId = ReadAttribute(sqsMessage, "CorrelationId") ?? string.Empty;
            ((IRawMessage)instance).FromRaw(bytes, correlationId);
            return instance;
        }

        var serializer = serializers.ResolveForContentType(contentType);
        return serializer.Deserialize(bytes, typeof(T)) as T;
    }

    /// <summary>
    ///     Extrai o payload, o content-type e o content-encoding: se for envelope do SNS, usa a mensagem
    ///     interna e os atributos do envelope; senão usa o corpo direto (ex.: webhook externo) e os atributos
    ///     da mensagem SQS.
    /// </summary>
    private static (string raw, string? contentType, string? contentEncoding) ExtractPayload(Message sqsMessage) {
        var attrContentType = ReadAttribute(sqsMessage, "Content-Type");
        var attrContentEncoding = ReadAttribute(sqsMessage, "Content-Encoding");

        if (AwsPubSubEnvelopeParser.IsSnsEnvelope(sqsMessage.Body)) {
            var envelope = AwsPubSubEnvelopeParser.Parse(sqsMessage.Body);
            return (envelope.Message, envelope.ContentType ?? attrContentType,
                envelope.ContentEncoding ?? attrContentEncoding);
        }

        return (sqsMessage.Body, attrContentType, attrContentEncoding);
    }

    private static string? ReadAttribute(Message sqsMessage, string name) =>
        sqsMessage.MessageAttributes is not null
        && sqsMessage.MessageAttributes.TryGetValue(name, out var value)
            ? value.StringValue
            : null;
}
