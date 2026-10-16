using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Base dos serviços Azure Service Bus: mantém um <see cref="ServiceBusClient" /> único e recuperável
///     (com retry exponencial) e oferece os utilitários de nomeação/roteamento. No modelo do Aedis, um exchange não vazio é tratado como Topic (pub/sub) e
///     um exchange vazio como Queue (point-to-point).
/// </summary>
public abstract class ServiceBusBaseService : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ILogger _logger;
    /// <summary>Configuração de conexão ao Service Bus usada pelas subclasses.</summary>
    protected readonly ServiceBusOptions _options;

    private ServiceBusClient? _client;

    /// <summary>
    ///     Prepara o serviço; o <see cref="ServiceBusClient" /> é criado de forma preguiçosa no primeiro
    ///     uso e recriado por <see cref="GetClientAsync" /> se estiver fechado.
    /// </summary>
    protected ServiceBusBaseService(IOptions<ServiceBusOptions> options, ILogger logger) {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Devolve o <see cref="ServiceBusClient" /> compartilhado, criando-o de forma preguiçosa e
    ///     thread-safe e reaproveitando-o enquanto não estiver fechado. Configura o retry exponencial.
    /// </summary>
    public async Task<ServiceBusClient> GetClientAsync() {
        if (_client is { IsClosed: false })
            return _client;

        await _connectionLock.WaitAsync();
        try {
            if (_client is { IsClosed: false })
                return _client;

            var clientOptions = new ServiceBusClientOptions {
                RetryOptions = new ServiceBusRetryOptions {
                    Mode = ServiceBusRetryMode.Exponential,
                    MaxRetries = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(5)
                }
            };

            _client = new ServiceBusClient(_options.ConnectionString, clientOptions);
            _logger.LogDebug("Nova conexão com o Azure Service Bus estabelecida.");
            return _client;
        }
        finally {
            _connectionLock.Release();
        }
    }

    /// <summary>Exchange não vazio → Topic (pub/sub); vazio → Queue (point-to-point).</summary>
    public static bool IsTopic(string? exchange) => !string.IsNullOrWhiteSpace(exchange);

    /// <summary>Normaliza o nome (minúsculas, espaços e underscores viram hífen) para o Service Bus.</summary>
    public static string NormalizeName(string name) => name.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');

    /// <summary>Descarta o cliente e libera o semáforo de conexão.</summary>
    public virtual async ValueTask DisposeAsync() {
        if (_client != null) {
            await _client.DisposeAsync();
            _client = null;
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

}
