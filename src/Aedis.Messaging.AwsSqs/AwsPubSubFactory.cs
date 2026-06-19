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
    /// <summary>Devolve o cliente SQS compartilhado, criando-o no primeiro uso (thread-safe).</summary>
    Task<IAmazonSQS> GetSqsClientAsync(CancellationToken ct = default);

    /// <summary>Devolve o cliente SNS compartilhado, criando-o no primeiro uso (thread-safe).</summary>
    Task<IAmazonSimpleNotificationService> GetSnsClientAsync(CancellationToken ct = default);

    /// <summary>
    ///     Detecta se o exchange é um SNS Topic (pub/sub) ou uma SQS Queue (point-to-point), com cache.
    ///     Consulta a API para descobrir o recurso e cai no default de <see cref="AwsSqsOptions.UseTopics" />
    ///     quando ele ainda não existe.
    /// </summary>
    Task<AwsSqsBaseService.ExchangeType> DetectExchangeTypeAsync(string exchange, CancellationToken ct = default);

    /// <summary>Limpa o cache de tipos de exchange — útil em testes ou após recriar recursos.</summary>
    void ClearExchangeTypeCache();

    /// <summary>Indica se o nome corresponde a uma fila/tópico FIFO (sufixo <c>.fifo</c>).</summary>
    bool IsFifoQueue(string name);

    /// <summary>Normaliza o nome para as convenções AWS (minúsculas, caracteres inválidos viram hífen).</summary>
    string NormalizeName(string name);
}

/// <summary>Implementação singleton da factory — reusa os clientes da <see cref="AwsSqsBaseService" />.</summary>
public sealed class AwsPubSubFactory : AwsSqsBaseService, IAwsPubSubFactory
{
    /// <summary>Cria a factory singleton, que reusa os clientes SQS/SNS preguiçosos da base.</summary>
    public AwsPubSubFactory(IOptions<AwsSqsOptions> options, ILogger<AwsPubSubFactory> logger)
        : base(options, logger) { }
}
