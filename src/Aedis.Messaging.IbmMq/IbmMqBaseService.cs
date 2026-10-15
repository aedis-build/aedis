using IBM.WMQ;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Base dos serviços IBM MQ do Aedis: dono do teto de conexões do processo
///     (<see cref="IbmMqConnectionBudget" />) e do pool de publicação. Cada publicação empresta uma conexão
///     exclusiva, faz <c>Put + Commit</c> e a devolve — nenhum commit toca a mensagem em voo de outro fluxo.
///     Nada conecta no construtor: a primeira conexão nasce no primeiro publish/health check.
/// </summary>
public abstract class IbmMqBaseService : IAsyncDisposable
{
    /// <summary>Logger compartilhado com as subclasses para diagnósticos de conexão e fila.</summary>
    protected readonly ILogger _logger;

    /// <summary>Configuração do provider usada pelas subclasses.</summary>
    protected readonly IbmMqOptions _options;

    private readonly IbmMqConnectionPool _publisherPool;
    private bool _disposed;

    /// <summary>
    ///     Valida a configuração, cria o teto de conexões e reserva nele o pool do publisher — o primeiro a
    ///     reservar, antes de qualquer consumer.
    /// </summary>
    protected IbmMqBaseService(IOptions<IbmMqOptions> options, ILogger logger) {
        _options = options.Value;
        _logger = logger;

        ValidateConnection();

        ConnectionBudget = new IbmMqConnectionBudget(_options.MaxConnections);
        var publisherSize = ConnectionBudget.Reserve("publisher", _options.PublisherPoolSize);
        _publisherPool = new IbmMqConnectionPool(_options, logger, publisherSize);

        _logger.LogDebug(
            "IBM MQ inicializado: teto de conexões do processo={Total}, publisher={Publisher}, restam {Available} para consumers.",
            ConnectionBudget.Total, publisherSize, ConnectionBudget.Available);
    }

    /// <summary>Teto de conexões do processo, compartilhado com o gerenciador de consumers.</summary>
    protected IbmMqConnectionBudget ConnectionBudget { get; }

    /// <summary>Fecha o pool de publicação (idempotente).</summary>
    public virtual ValueTask DisposeAsync() {
        if (_disposed) return ValueTask.CompletedTask;

        _publisherPool.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Empresta uma conexão do pool de publicação, executa a operação e a devolve — a unidade de trabalho
    ///     (PUT e commit) acontece inteira na mesma conexão exclusiva.
    /// </summary>
    protected async Task ExecuteWithSessionAsync(Func<MQQueueManager, Task> operation,
        CancellationToken cancellationToken = default) {
        var connection = await _publisherPool.CheckoutAsync(cancellationToken);
        try {
            await operation(connection);
        }
        finally {
            _publisherPool.Checkin(connection);
        }
    }

    /// <summary>Variante de <see cref="ExecuteWithSessionAsync(Func{MQQueueManager,Task},CancellationToken)" /> que devolve o resultado.</summary>
    protected async Task<T> ExecuteWithSessionAsync<T>(Func<MQQueueManager, Task<T>> operation,
        CancellationToken cancellationToken = default) {
        var connection = await _publisherPool.CheckoutAsync(cancellationToken);
        try {
            return await operation(connection);
        }
        finally {
            _publisherPool.Checkin(connection);
        }
    }

    /// <summary>
    ///     Prova conectividade emprestando e devolvendo uma conexão do pool de publicação. Devolve
    ///     <c>true</c> quando uma conexão pôde ser estabelecida; usado pelo health check.
    /// </summary>
    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default) {
        try {
            var connection = await _publisherPool.CheckoutAsync(cancellationToken);
            _publisherPool.Checkin(connection);
            return true;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Falha ao estabelecer conexão com o IBM MQ ({QueueManager}).", _options.QueueManager);
            return false;
        }
    }

    private void ValidateConnection() {
        if (string.IsNullOrWhiteSpace(_options.QueueManager))
            throw new InvalidOperationException("IBMMQ:QueueManager não pode ser nulo ou vazio.");

        if (string.IsNullOrWhiteSpace(_options.ConnectionNameList))
            throw new InvalidOperationException("IBMMQ:ConnectionNameList não pode ser nulo ou vazio.");

        if (string.IsNullOrWhiteSpace(_options.Channel))
            throw new InvalidOperationException("IBMMQ:Channel não pode ser nulo ou vazio.");

        if (_options.MaxConnections <= _options.PublisherPoolSize)
            throw new InvalidOperationException(
                $"IBMMQ:MaxConnections ({_options.MaxConnections}) precisa ser maior que IBMMQ:PublisherPoolSize " +
                $"({_options.PublisherPoolSize}) para sobrar ao menos uma conexão para consumers.");

        if (_options.EnableDeadLetterQueue && string.IsNullOrWhiteSpace(_options.DeadLetterQueueName))
            throw new InvalidOperationException("IBMMQ:DeadLetterQueueName é obrigatório quando EnableDeadLetterQueue está ligado.");
    }

    /// <summary>Abre a fila para escrita e consulta, falhando se o broker estiver em quiesce. Use dentro de <c>using</c>.</summary>
    protected static MQQueue OpenQueue(MQQueueManager queueManager, string queueName) {
        var queueOptions = MQC.MQOO_OUTPUT | MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING;
        return queueManager.AccessQueue(queueName, queueOptions);
    }

    /// <summary>Monta as opções de PUT (novo MsgId, falha em quiesce) com ou sem syncpoint conforme <see cref="IbmMqOptions.UseSyncpoint" />.</summary>
    protected MQPutMessageOptions BuildPutMessageOptions() {
        var options = MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_NEW_MSG_ID;
        options |= _options.UseSyncpoint ? MQC.MQPMO_SYNCPOINT : MQC.MQPMO_NO_SYNCPOINT;
        return new MQPutMessageOptions { Options = options };
    }
}
