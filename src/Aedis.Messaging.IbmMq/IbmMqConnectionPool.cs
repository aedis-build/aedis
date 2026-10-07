using System.Collections;
using System.Collections.Concurrent;
using IBM.WMQ;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Pool de conexões (<see cref="MQQueueManager" />) com checkout <strong>exclusivo</strong>: cada conexão
///     emprestada pertence a um único fluxo — um worker de consumer ou uma publicação — durante toda a unidade
///     de trabalho, do <c>Get</c>/<c>Put</c> ao <c>Commit</c>/<c>Backout</c>. Como o syncpoint do IBM MQ tem
///     escopo de conexão, esse isolamento garante que nenhum commit ou backout toque a mensagem em voo de outro
///     fluxo, e como cada conexão é tocada por uma thread por vez, elimina o acesso concorrente a um HCONN. As
///     conexões nascem sob demanda no primeiro checkout e usam <c>MQCNO_RECONNECT</c>.
/// </summary>
internal sealed class IbmMqConnectionPool : IDisposable
{
    private readonly SemaphoreSlim _capacity;
    private readonly ConcurrentBag<MQQueueManager> _idle = new();
    private readonly ILogger _logger;
    private readonly IbmMqOptions _options;
    private readonly List<MQQueueManager> _tracked = [];
    private readonly object _trackLock = new();
    private volatile bool _disposed;

    internal IbmMqConnectionPool(IbmMqOptions options, ILogger logger, int maxSize) {
        _options = options;
        _logger = logger;
        MaxSize = Math.Max(1, maxSize);
        _capacity = new SemaphoreSlim(MaxSize, MaxSize);
    }

    /// <summary>Número máximo de conexões emprestadas simultaneamente.</summary>
    internal int MaxSize { get; }

    /// <summary>
    ///     Fecha todas as conexões rastreadas, inclusive as ainda emprestadas (cenário de shutdown) — o que
    ///     destrava um worker preso num <c>Get</c> em espera sem aguardar o intervalo de WAIT.
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        List<MQQueueManager> toClose;
        lock (_trackLock) {
            toClose = [.. _tracked];
            _tracked.Clear();
        }

        foreach (var connection in toClose) SafeClose(connection);
        while (_idle.TryTake(out _)) { }

        _capacity.Dispose();
    }

    /// <summary>Empresta uma conexão, criando sob demanda até <see cref="MaxSize" />; aguarda quando todas estão emprestadas.</summary>
    internal async Task<MQQueueManager> CheckoutAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _capacity.WaitAsync(cancellationToken);
        return TakeOrCreate();
    }

    /// <summary>
    ///     Variante síncrona para quem roda em thread dedicada e não deve ceder a thread (workers de
    ///     consumer): o cliente IBM MQ é síncrono, então não há o que ganhar cedendo e há o que perder —
    ///     uma continuação no thread pool presa no connect ou no <c>Get</c> em espera.
    /// </summary>
    internal MQQueueManager Checkout(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _capacity.Wait(cancellationToken);
        return TakeOrCreate();
    }

    /// <summary>Devolve a conexão ao pool; conexões inválidas são descartadas para recriação no próximo checkout.</summary>
    internal void Checkin(MQQueueManager? connection) {
        if (connection is null) return;

        if (_disposed) {
            SafeClose(connection);
            return;
        }

        if (IsValid(connection)) _idle.Add(connection);
        else Destroy(connection);

        try {
            _capacity.Release();
        }
        catch (ObjectDisposedException) { }
        catch (SemaphoreFullException) { }
    }

    private MQQueueManager TakeOrCreate() {
        try {
            while (_idle.TryTake(out var pooled)) {
                if (IsValid(pooled)) return pooled;
                Destroy(pooled);
            }

            return Create();
        }
        catch {
            try {
                _capacity.Release();
            }
            catch (ObjectDisposedException) { }

            throw;
        }
    }

    private MQQueueManager Create() {
        var connection = new MQQueueManager(_options.QueueManager, BuildConnectionProps());

        lock (_trackLock) _tracked.Add(connection);

        _logger.LogDebug("Pool IBM MQ criou nova conexão para o QueueManager {QueueManager}.", _options.QueueManager);
        return connection;
    }

    private void Destroy(MQQueueManager connection) {
        lock (_trackLock) _tracked.Remove(connection);
        SafeClose(connection);
    }

    private static bool IsValid(MQQueueManager connection) {
        try {
            return connection.IsConnected;
        }
        catch {
            return false;
        }
    }

    private void SafeClose(MQQueueManager connection) {
        try {
            connection.Disconnect();
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Erro ao desconectar conexão IBM MQ do pool.");
        }

        try {
            connection.Close();
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Erro ao fechar conexão IBM MQ do pool.");
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
}
