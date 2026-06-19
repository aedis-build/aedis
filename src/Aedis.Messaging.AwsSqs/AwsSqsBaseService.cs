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
///     fila/tópico e detecta de forma transparente se um "exchange" é um SNS Topic (pub/sub) ou uma SQS
///     Queue (point-to-point), com cache. Credenciais explícitas são opcionais (senão usa a cadeia do
///     ambiente — IAM Role/IRSA).
/// </summary>
public abstract partial class AwsSqsBaseService : IAsyncDisposable
{
    /// <summary>Logger compartilhado com as subclasses para diagnósticos de conexão e publicação.</summary>
    protected readonly ILogger Logger;

    /// <summary>Configuração de acesso ao SQS/SNS usada pelas subclasses.</summary>
    protected readonly AwsSqsOptions Options;

    private readonly ConcurrentDictionary<string, ExchangeType> _exchangeTypeCache = new();
    private readonly SemaphoreSlim _snsClientLock = new(1, 1);
    private readonly SemaphoreSlim _sqsClientLock = new(1, 1);

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    /// <summary>
    ///     Prepara o serviço com as opções e o logger; os clientes SQS/SNS são criados de forma preguiçosa no
    ///     primeiro uso.
    /// </summary>
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

    /// <summary>
    ///     Devolve o cliente SQS único, criando-o de forma preguiçosa e thread-safe (double-checked lock)
    ///     no primeiro uso. Usa credenciais estáticas se informadas, senão a cadeia do ambiente.
    /// </summary>
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

    /// <summary>
    ///     Devolve o cliente SNS único, criando-o de forma preguiçosa e thread-safe (double-checked lock)
    ///     no primeiro uso. Usa credenciais estáticas se informadas, senão a cadeia do ambiente.
    /// </summary>
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

    /// <summary>Normaliza o nome (minúsculas, caracteres inválidos viram hífen) para as convenções AWS.</summary>
    public string NormalizeName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome não pode ser nulo ou vazio.", nameof(name));

        var normalized = InvalidChars().Replace(name.Trim().ToLowerInvariant(), "-");
        normalized = MultipleHyphens().Replace(normalized, "-");
        return normalized.Trim('-');
    }

    /// <summary>Indica se o nome corresponde a uma fila/tópico FIFO (sufixo <c>.fifo</c>).</summary>
    public bool IsFifoQueue(string name) => name.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Detecta se o exchange é uma SQS Queue (consulta GetQueueUrl, permissão restrita ao recurso) ou,
    ///     caso não exista, assume o default de <see cref="AwsSqsOptions.UseTopics" />. Resultado em cache.
    /// </summary>
    public async Task<ExchangeType> DetectExchangeTypeAsync(string exchange, CancellationToken ct = default) {
        var normalized = NormalizeName(exchange);

        if (_exchangeTypeCache.TryGetValue(normalized, out var cached))
            return cached;

        try {
            var sqsClient = await GetSqsClientAsync(ct);
            await sqsClient.GetQueueUrlAsync(normalized, ct);
            _exchangeTypeCache[normalized] = ExchangeType.Queue;
            Logger.LogDebug("Exchange '{Exchange}' detectado como SQS Queue.", normalized);
            return ExchangeType.Queue;
        }
        catch (QueueDoesNotExistException) {
        }
        catch (Exception ex) {
            Logger.LogWarning(ex, "Erro ao verificar a fila SQS '{Exchange}'.", normalized);
        }

        var defaultType = Options.UseTopics ? ExchangeType.Topic : ExchangeType.Queue;
        _exchangeTypeCache[normalized] = defaultType;
        Logger.LogDebug("Exchange '{Exchange}' não encontrado — usando o default {Type} (UseTopics={UseTopics}).",
            normalized, defaultType, Options.UseTopics);
        return defaultType;
    }

    /// <summary>Limpa o cache de tipos de exchange — útil em testes ou após recriar recursos.</summary>
    public void ClearExchangeTypeCache() => _exchangeTypeCache.Clear();

    /// <summary>Descarta os clientes SQS/SNS e os semáforos de inicialização.</summary>
    public async ValueTask DisposeAsync() {
        _sqsClient?.Dispose();
        _snsClient?.Dispose();
        _sqsClientLock.Dispose();
        _snsClientLock.Dispose();
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
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
