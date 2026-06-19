using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Auto-provisiona os recursos AWS necessários: cria filas (com DLQ via RedrivePolicy), tópicos SNS,
///     inscrições fila→tópico (com filter policy) e a política que autoriza o SNS a entregar na fila.
///     Operações idempotentes; ARNs em cache.
/// </summary>
public sealed class AwsSqsAdministrationHelper(
    IAwsPubSubFactory factory,
    IOptions<AwsSqsOptions> options,
    ILogger<AwsSqsAdministrationHelper> logger)
{
    private const string RetentionFourteenDays = "1209600";
    private readonly AwsSqsOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, string> _queueArnCache = new();
    private readonly ConcurrentDictionary<string, string> _topicArnCache = new();

    /// <summary>
    ///     Garante a existência da fila SQS (idempotente) e devolve sua URL. Quando <paramref name="withDlq" />
    ///     é verdadeiro, provisiona também a DLQ e a vincula via RedrivePolicy. Respeita o sufixo <c>.fifo</c>
    ///     e os atributos FIFO quando <see cref="AwsSqsOptions.UseFifoQueues" /> está ligado.
    /// </summary>
    public async Task<string> EnsureQueueExistsAsync(string queueName, bool withDlq = true,
        CancellationToken ct = default) {
        var name = factory.NormalizeName(queueName);
        if (_options.UseFifoQueues && !factory.IsFifoQueue(name))
            name += ".fifo";

        var sqsClient = await factory.GetSqsClientAsync(ct);

        try {
            var existing = await sqsClient.GetQueueUrlAsync(name, ct);
            return existing.QueueUrl;
        }
        catch (QueueDoesNotExistException) {
        }

        string? dlqArn = null;
        if (withDlq)
            dlqArn = await CreateDlqAsync($"{name}-dlq", factory.IsFifoQueue(name), ct);

        var attributes = new Dictionary<string, string> {
            ["MessageRetentionPeriod"] = RetentionFourteenDays,
            ["VisibilityTimeout"] = _options.VisibilityTimeout.ToString(),
            ["ReceiveMessageWaitTimeSeconds"] = _options.WaitTimeSeconds.ToString()
        };

        if (dlqArn != null)
            attributes["RedrivePolicy"] = JsonSerializer.Serialize(new {
                maxReceiveCount = _options.MaxRetries,
                deadLetterTargetArn = dlqArn
            });

        if (factory.IsFifoQueue(name)) {
            attributes["FifoQueue"] = "true";
            attributes["ContentBasedDeduplication"] = "true";
        }

        var created = await sqsClient.CreateQueueAsync(new CreateQueueRequest {
            QueueName = name,
            Attributes = attributes
        }, ct);

        logger.LogDebug("Fila SQS '{QueueName}' criada: {QueueUrl}", name, created.QueueUrl);
        return created.QueueUrl;
    }

    /// <summary>
    ///     Garante a existência do tópico SNS (idempotente na AWS) e devolve seu ARN, com cache. Aplica os
    ///     atributos FIFO quando o nome termina em <c>.fifo</c>.
    /// </summary>
    public async Task<string> EnsureTopicExistsAsync(string topicName, CancellationToken ct = default) {
        var name = factory.NormalizeName(topicName);
        if (_options.UseFifoQueues && !name.EndsWith(".fifo"))
            name += ".fifo";

        if (_topicArnCache.TryGetValue(name, out var cached))
            return cached;

        var snsClient = await factory.GetSnsClientAsync(ct);
        var request = new CreateTopicRequest { Name = name };

        if (factory.IsFifoQueue(name))
            request.Attributes = new Dictionary<string, string> {
                ["FifoTopic"] = "true",
                ["ContentBasedDeduplication"] = "true"
            };

        var created = await snsClient.CreateTopicAsync(request, ct);
        logger.LogDebug("Tópico SNS '{TopicName}' garantido: {TopicArn}", name, created.TopicArn);

        _topicArnCache[name] = created.TopicArn;
        return created.TopicArn;
    }

    /// <summary>
    ///     Inscreve a fila no tópico SNS (protocolo <c>sqs</c>), aplicando a filter policy opcional, e
    ///     concede ao SNS a permissão de entregar na fila. Devolve o ARN da inscrição.
    /// </summary>
    public async Task<string> SubscribeQueueToTopicAsync(string queueArn, string topicArn, string? filterPolicy = null,
        CancellationToken ct = default) {
        var snsClient = await factory.GetSnsClientAsync(ct);
        var sqsClient = await factory.GetSqsClientAsync(ct);

        var request = new SubscribeRequest {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn
        };

        if (!string.IsNullOrWhiteSpace(filterPolicy))
            request.Attributes = new Dictionary<string, string> { ["FilterPolicy"] = filterPolicy };

        var response = await snsClient.SubscribeAsync(request, ct);

        var queueUrl = await GetQueueUrlFromArnAsync(queueArn, sqsClient, ct);
        await AddSnsPermissionToQueueAsync(queueUrl, topicArn, sqsClient, ct);

        logger.LogDebug("Fila {QueueArn} inscrita no tópico {TopicArn}.", queueArn, topicArn);
        return response.SubscriptionArn;
    }

    /// <summary>Resolve o ARN da fila a partir do nome (com cache), consultando seus atributos no SQS.</summary>
    public async Task<string> GetQueueArnAsync(string queueName, CancellationToken ct = default) {
        var name = factory.NormalizeName(queueName);
        if (_queueArnCache.TryGetValue(name, out var cached))
            return cached;

        var sqsClient = await factory.GetSqsClientAsync(ct);
        var queueUrl = await sqsClient.GetQueueUrlAsync(name, ct);
        var attributes = await sqsClient.GetQueueAttributesAsync(queueUrl.QueueUrl, ["QueueArn"], ct);

        _queueArnCache[name] = attributes.QueueARN;
        return attributes.QueueARN;
    }

    private async Task<string> CreateDlqAsync(string dlqName, bool isFifo, CancellationToken ct) {
        var sqsClient = await factory.GetSqsClientAsync(ct);

        try {
            var existing = await sqsClient.GetQueueUrlAsync(dlqName, ct);
            var attrs = await sqsClient.GetQueueAttributesAsync(existing.QueueUrl, ["QueueArn"], ct);
            return attrs.QueueARN;
        }
        catch (QueueDoesNotExistException) {
        }

        var attributes = new Dictionary<string, string> { ["MessageRetentionPeriod"] = RetentionFourteenDays };
        if (isFifo) {
            attributes["FifoQueue"] = "true";
            attributes["ContentBasedDeduplication"] = "true";
        }

        var created = await sqsClient.CreateQueueAsync(new CreateQueueRequest {
            QueueName = dlqName,
            Attributes = attributes
        }, ct);
        var dlqAttrs = await sqsClient.GetQueueAttributesAsync(created.QueueUrl, ["QueueArn"], ct);

        logger.LogDebug("DLQ '{DlqName}' criada: {QueueArn}", dlqName, dlqAttrs.QueueARN);
        return dlqAttrs.QueueARN;
    }

    private static async Task<string> GetQueueUrlFromArnAsync(string queueArn, IAmazonSQS sqsClient,
        CancellationToken ct) {
        var queueName = queueArn.Split(':').Last();
        var response = await sqsClient.GetQueueUrlAsync(queueName, ct);
        return response.QueueUrl;
    }

    private static async Task AddSnsPermissionToQueueAsync(string queueUrl, string topicArn, IAmazonSQS sqsClient,
        CancellationToken ct) {
        var attributes = await sqsClient.GetQueueAttributesAsync(queueUrl, ["QueueArn", "Policy"], ct);
        var queueArn = attributes.QueueARN;

        var policy = new {
            Version = "2012-10-17",
            Statement = new[] {
                new {
                    Effect = "Allow",
                    Principal = new { Service = "sns.amazonaws.com" },
                    Action = "sqs:SendMessage",
                    Resource = queueArn,
                    Condition = new Dictionary<string, object> {
                        ["ArnEquals"] = new Dictionary<string, string> { ["aws:SourceArn"] = topicArn }
                    }
                }
            }
        };

        await sqsClient.SetQueueAttributesAsync(queueUrl, new Dictionary<string, string> {
            ["Policy"] = JsonSerializer.Serialize(policy)
        }, ct);
    }
}
