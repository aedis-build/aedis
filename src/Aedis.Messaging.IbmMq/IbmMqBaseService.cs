using System.Collections;
using IBM.WMQ;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Base dos serviços IBM MQ do Aedis: mantém uma única conexão (<see cref="MQQueueManager" />) por
///     instância, com criação preguiçosa (no primeiro uso), reconexão automática quando a conexão é
///     detectada inválida e descarte ordenado. Os GET/PUT operam sob syncpoint nas subclasses.
/// </summary>
public abstract class IbmMqBaseService : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    protected readonly ILogger _logger;
    protected readonly IbmMqOptions _options;

    private MQQueueManager? _connection;
    private bool _disposed;

    protected IbmMqBaseService(IOptions<IbmMqOptions> options, ILogger logger) {
        _options = options.Value;
        _logger = logger;

        ValidateConnection();

        _logger.LogDebug("IbmMqBaseService inicializado. A conexão será estabelecida no primeiro uso.");
    }

    public virtual async ValueTask DisposeAsync() {
        if (_disposed) return;

        _logger.LogDebug("Descartando IbmMqBaseService.");

        await _connectionLock.WaitAsync();
        try {
            if (_connection != null) {
                try {
                    _connection.Disconnect();
                    _logger.LogDebug("Conexão IBM MQ desconectada.");
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "Erro ao desconectar do IBM MQ.");
                }

                try {
                    _connection.Close();
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "Erro ao fechar a conexão IBM MQ.");
                }

                _connection = null;
            }
        }
        finally {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    protected async Task ExecuteWithSessionAsync(Func<MQQueueManager, Task> operation,
        CancellationToken cancellationToken = default) {
        var connection = await EnsureConnectionAsync();
        await operation(connection);
    }

    protected async Task<T> ExecuteWithSessionAsync<T>(Func<MQQueueManager, Task<T>> operation,
        CancellationToken cancellationToken = default) {
        var connection = await EnsureConnectionAsync();
        return await operation(connection);
    }

    public async Task<MQQueueManager> EnsureConnectionAsync() {
        await _connectionLock.WaitAsync();
        try {
            if (_connection != null && IsConnectionValid(_connection)) return _connection;

            if (_connection != null) {
                _logger.LogWarning("Conexão IBM MQ inválida detectada. Recriando a conexão.");
                try {
                    _connection.Disconnect();
                }
                catch {
                    // ignorado: a conexão já está em estado inválido
                }

                try {
                    _connection.Close();
                }
                catch {
                    // ignorado: a conexão já está em estado inválido
                }

                _connection = null;
            }

            _logger.LogDebug("Estabelecendo conexão com o IBM MQ...");

            var props = BuildConnectionProps();
            _connection = new MQQueueManager(_options.QueueManager, props);

            _logger.LogDebug("Conectado ao IBM MQ: QueueManager={QueueManager}, Host={ConnectionNameList}.",
                _options.QueueManager, _options.ConnectionNameList);

            return _connection;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Falha ao conectar ao IBM MQ: QueueManager={QueueManager}.", _options.QueueManager);
            throw;
        }
        finally {
            _connectionLock.Release();
        }
    }

    public Task<bool> IsConnectionHealthyAsync() {
        try {
            return Task.FromResult(_connection != null && IsConnectionValid(_connection));
        }
        catch {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Tenta estabelecer a conexão com o IBM MQ. Devolve <c>true</c> se conectado, <c>false</c> se
    ///     falhou. Usado pelo health check para refletir o estado real da conexão.
    /// </summary>
    public async Task<bool> TryEnsureConnectionAsync() {
        try {
            await EnsureConnectionAsync();
            return true;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Falha ao tentar estabelecer conexão com o IBM MQ.");
            return false;
        }
    }

    private void ValidateConnection() {
        if (string.IsNullOrWhiteSpace(_options.QueueManager))
            throw new InvalidOperationException("QueueManager não pode ser nulo ou vazio.");

        if (string.IsNullOrWhiteSpace(_options.ConnectionNameList))
            throw new InvalidOperationException("ConnectionNameList não pode ser nulo ou vazio.");

        if (string.IsNullOrWhiteSpace(_options.Channel))
            throw new InvalidOperationException("Channel não pode ser nulo ou vazio.");

        _logger.LogDebug("Configuração IBM MQ validada para o QueueManager {QueueManager}.", _options.QueueManager);
    }

    private static bool IsConnectionValid(MQQueueManager connection) {
        try {
            return connection.IsConnected;
        }
        catch {
            return false;
        }
    }

    private Hashtable BuildConnectionProps() {
        var props = new Hashtable {
            [MQC.CONNECTION_NAME_PROPERTY] = _options.ConnectionNameList,
            [MQC.CHANNEL_PROPERTY] = _options.Channel,
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED,
            [MQC.CONNECT_OPTIONS_PROPERTY] = MQC.MQCNO_RECONNECT
        };

        if (!string.IsNullOrWhiteSpace(_options.UserId)) {
            props[MQC.USER_ID_PROPERTY] = _options.UserId;
            props[MQC.USE_MQCSP_AUTHENTICATION_PROPERTY] = true;
        }

        if (!string.IsNullOrWhiteSpace(_options.Password))
            props[MQC.PASSWORD_PROPERTY] = _options.Password;

        return props;
    }

    protected static MQQueue OpenQueue(MQQueueManager queueManager, string queueName) {
        var queueOptions = MQC.MQOO_OUTPUT | MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING;
        return queueManager.AccessQueue(queueName, queueOptions);
    }

    protected MQPutMessageOptions BuildPutMessageOptions() {
        var options = MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_NEW_MSG_ID;
        options |= _options.UseSyncpoint ? MQC.MQPMO_SYNCPOINT : MQC.MQPMO_NO_SYNCPOINT;
        return new MQPutMessageOptions { Options = options };
    }
}
