using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Base dos serviços AWS SQS/SNS: mantém clientes SQS e SNS únicos (thread-safe), normaliza nomes de
///     fila/tópico (preservando o sufixo <c>.fifo</c> e aplicando-o quando <see cref="AwsSqsOptions.UseFifoQueues" />
///     está ligado) e resolve se um "exchange" é um SNS Topic ou uma SQS Queue com o mínimo de permissões:
///     nunca <c>ListQueues</c>/<c>ListTopics</c>. Credenciais explícitas são opcionais (senão usa a cadeia do
///     ambiente — IAM Role/IRSA).
/// </summary>
public abstract partial class AwsSqsBaseService : IAsyncDisposable
{
    private const string FifoSuffix = ".fifo";

    /// <summary>Logger compartilhado com as subclasses para diagnósticos de conexão e publicação.</summary>
    protected readonly ILogger Logger;

    /// <summary>Configuração de acesso ao SQS/SNS usada pelas subclasses.</summary>
    protected readonly AwsSqsOptions Options;

    private readonly ConcurrentDictionary<string, ExchangeType> _exchangeTypeCache = new();
    private readonly SemaphoreSlim _snsClientLock = new(1, 1);
    private readonly SemaphoreSlim _sqsClientLock = new(1, 1);

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    /// <summary>Prepara o serviço com as opções e o logger; os clientes SQS/SNS são criados no primeiro uso.</summary>
    protected AwsSqsBaseService(IOptions<AwsSqsOptions> options, ILogger logger) {
        Options = options.Value;
        Logger = logger;
    }

    /// <summary>Tipo do exchange, detectado de forma transparente ao usuário.</summary>
    public enum ExchangeType
    {
        /// <summary>SNS Topic — semântica pub/sub (fan-out para múltiplas filas inscritas).</summary>
        Topic,

        /// <summary>SQS Queue — semântica point-to-point (uma fila, um consumidor lógico).</summary>
        Queue
    }

    /// <summary>Devolve o cliente SQS único, criando-o de forma preguiçosa e thread-safe no primeiro uso.</summary>
    public async Task<IAmazonSQS> GetSqsClientAsync(CancellationToken ct = default) {
        if (_sqsClient != null) return _sqsClient;

        await _sqsClientLock.WaitAsync(ct);
        try {
            if (_sqsClient != null) return _sqsClient;

            var config = new AmazonSQSConfig { Timeout = TimeSpan.FromSeconds(Options.ConnectionTimeoutSeconds) };
            ApplyEndpoint(config);

            _sqsClient = HasStaticCredentials()
                ? new AmazonSQSClient(Options.AccessKeyId, Options.SecretAccessKey, config)
                : new AmazonSQSClient(config);

            Logger.LogDebug("Cliente AWS SQS inicializado (região {Region}).", Options.Region ?? "default");
            return _sqsClient;
        }
        finally {
            _sqsClientLock.Release();
        }
    }

    /// <summary>Devolve o cliente SNS único, criando-o de forma preguiçosa e thread-safe no primeiro uso.</summary>
    public async Task<IAmazonSimpleNotificationService> GetSnsClientAsync(CancellationToken ct = default) {
        if (_snsClient != null) return _snsClient;

        await _snsClientLock.WaitAsync(ct);
        try {
            if (_snsClient != null) return _snsClient;

            var config = new AmazonSimpleNotificationServiceConfig {
                Timeout = TimeSpan.FromSeconds(Options.ConnectionTimeoutSeconds)
            };
            ApplyEndpoint(config);

            _snsClient = HasStaticCredentials()
                ? new AmazonSimpleNotificationServiceClient(Options.AccessKeyId, Options.SecretAccessKey, config)
                : new AmazonSimpleNotificationServiceClient(config);

            Logger.LogDebug("Cliente AWS SNS inicializado (região {Region}).", Options.Region ?? "default");
            return _snsClient;
        }
        finally {
            _snsClientLock.Release();
        }
    }

    /// <summary>
    ///     Normaliza o nome para as convenções AWS (minúsculas, caracteres inválidos viram hífen), preservando
    ///     um sufixo <c>.fifo</c> explícito.
    /// </summary>
    public string NormalizeName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome não pode ser nulo ou vazio.", nameof(name));

        var trimmed = name.Trim();
        var fifo = trimmed.EndsWith(FifoSuffix, StringComparison.OrdinalIgnoreCase);
        if (fifo) trimmed = trimmed[..^FifoSuffix.Length];

        var normalized = InvalidChars().Replace(trimmed.ToLowerInvariant(), "-");
        normalized = MultipleHyphens().Replace(normalized, "-").Trim('-');
        return fifo ? normalized + FifoSuffix : normalized;
    }

    /// <summary>
    ///     Nome efetivo do recurso: normalizado e com o sufixo <c>.fifo</c> aplicado quando
    ///     <see cref="AwsSqsOptions.UseFifoQueues" /> está ligado. É o único nome usado para criar, consultar e
    ///     consumir — publisher, admin e consumer concordam sempre.
    /// </summary>
    public string ResolveQueueName(string name) {
        var normalized = NormalizeName(name);
        return Options.UseFifoQueues && !IsFifoQueue(normalized) ? normalized + FifoSuffix : normalized;
    }

    /// <summary>Indica se o nome corresponde a uma fila/tópico FIFO (sufixo <c>.fifo</c>).</summary>
    public bool IsFifoQueue(string name) => name.EndsWith(FifoSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Resolve o tipo do exchange com o mínimo de permissões. Com <see cref="AwsSqsOptions.UseTopics" />
    ///     ligado, é Topic por configuração — sem sondar o SQS. Desligado, sonda a fila por
    ///     <c>GetQueueUrl</c> (permissão restrita ao recurso) e cai em Queue se ela não existe. Em cache.
    /// </summary>
    public async Task<ExchangeType> DetectExchangeTypeAsync(string exchange, CancellationToken ct = default) {
        var name = ResolveQueueName(exchange);

        if (_exchangeTypeCache.TryGetValue(name, out var cached))
            return cached;

        if (Options.UseTopics) {
            _exchangeTypeCache[name] = ExchangeType.Topic;
            Logger.LogDebug("Exchange '{Exchange}' resolvido como SNS Topic por configuração (UseTopics=true).", name);
            return ExchangeType.Topic;
        }

        try {
            var sqsClient = await GetSqsClientAsync(ct);
            await sqsClient.GetQueueUrlAsync(name, ct);
            Logger.LogDebug("Exchange '{Exchange}' detectado como SQS Queue existente.", name);
        }
        catch (QueueDoesNotExistException) {
            Logger.LogDebug("Exchange '{Exchange}' ainda não existe; tratado como SQS Queue.", name);
        }
        catch (Exception ex) {
            Logger.LogWarning(ex, "Erro ao verificar a fila SQS '{Exchange}'; tratado como SQS Queue.", name);
        }

        _exchangeTypeCache[name] = ExchangeType.Queue;
        return ExchangeType.Queue;
    }

    /// <summary>Limpa o cache de tipos de exchange — útil em testes ou após recriar recursos.</summary>
    public void ClearExchangeTypeCache() => _exchangeTypeCache.Clear();

    /// <summary>Descarta os clientes SQS/SNS e os semáforos de inicialização.</summary>
    public ValueTask DisposeAsync() {
        _sqsClient?.Dispose();
        _snsClient?.Dispose();
        _sqsClientLock.Dispose();
        _snsClientLock.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private bool HasStaticCredentials() =>
        !string.IsNullOrWhiteSpace(Options.AccessKeyId) && !string.IsNullOrWhiteSpace(Options.SecretAccessKey);

    private void ApplyEndpoint(ClientConfig config) {
        if (!string.IsNullOrWhiteSpace(Options.ServiceUrl))
            config.ServiceURL = Options.ServiceUrl;
        else if (!string.IsNullOrWhiteSpace(Options.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(Options.Region);
    }

    [GeneratedRegex(@"[^a-z0-9\-_]")]
    private static partial Regex InvalidChars();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleHyphens();
}
