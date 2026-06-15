using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Factory singleton dos clientes AWS SQS/SNS — uma única conexão compartilhada entre o broker, o
///     consumer manager, o admin helper e o health check.
/// </summary>
public interface IAwsPubSubFactory
{
    Task<IAmazonSQS> GetSqsClientAsync(CancellationToken ct = default);
    Task<IAmazonSimpleNotificationService> GetSnsClientAsync(CancellationToken ct = default);
    Task<AwsSqsBaseService.ExchangeType> DetectExchangeTypeAsync(string exchange, CancellationToken ct = default);
    void ClearExchangeTypeCache();
    bool IsFifoQueue(string name);
    string NormalizeName(string name);
}

/// <summary>Implementação singleton da factory — reusa os clientes da <see cref="AwsSqsBaseService" />.</summary>
public sealed class AwsPubSubFactory : AwsSqsBaseService, IAwsPubSubFactory
{
    public AwsPubSubFactory(IOptions<AwsSqsOptions> options, ILogger<AwsPubSubFactory> logger)
        : base(options, logger) { }
}
