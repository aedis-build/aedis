using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Base dos serviços Azure Service Bus: mantém um <see cref="ServiceBusClient" /> único e recuperável
///     (com retry exponencial), monitora a saúde da conexão por timer e oferece os utilitários de
///     nomeação/roteamento. No modelo do Aedis, um exchange não vazio é tratado como Topic (pub/sub) e
///     um exchange vazio como Queue (point-to-point).
/// </summary>
public abstract class ServiceBusBaseService : IAsyncDisposable
{
    private readonly Timer _connectionHealthTimer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ILogger _logger;
    protected readonly ServiceBusOptions _options;

    private ServiceBusClient? _client;
    private volatile bool _isConnectionHealthy = true;

    protected ServiceBusBaseService(IOptions<ServiceBusOptions> options, ILogger logger) {
        _options = options.Value;
        _logger = logger;
        _connectionHealthTimer = new Timer(CheckConnectionHealth, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool IsConnectionHealthy => _isConnectionHealthy;

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

    public virtual async ValueTask DisposeAsync() {
        await _connectionHealthTimer.DisposeAsync();

        if (_client != null) {
            await _client.DisposeAsync();
            _client = null;
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private void CheckConnectionHealth(object? state) {
        try {
            if (_client is null || _client.IsClosed) {
                if (!_isConnectionHealthy) return;

                _logger.LogWarning("Conexão com o Azure Service Bus não está saudável. Tentando reconectar...");
                _isConnectionHealthy = false;

                _ = Task.Run(async () => {
                    try {
                        await GetClientAsync();
                        _isConnectionHealthy = true;
                        _logger.LogDebug("Conexão com o Azure Service Bus restaurada.");
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Falha ao restaurar a conexão com o Azure Service Bus.");
                    }
                });
            }
            else if (!_isConnectionHealthy) {
                _logger.LogDebug("Conexão com o Azure Service Bus saudável novamente.");
                _isConnectionHealthy = true;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Erro ao verificar a saúde da conexão com o Azure Service Bus.");
        }
    }
}
