using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Aedis.Core.Utils;
using Aedis.Messaging.Abstractions;
using Aedis.Observability.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aedis.Messaging.BusinessEvents;

/// <summary>
///     Contrato publicado na trilha de auditoria para cada evento de negócio marcado como auditável. É uma
///     <see cref="IAuditableMessage" />: <see cref="AuditableMessageBase.Action" /> é a ação do evento,
///     <see cref="AuditableMessageBase.Subject" /> o processo e <see cref="AuditableMessageBase.Author" /> quem
///     pediu. Os atributos viajam como texto já mascarado pela política de redaction do log.
/// </summary>
public sealed class BusinessEventMessage : AuditableMessageBase
{
    /// <summary>Versão do contrato; incrementa em mudança incompatível.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary><c>event.name</c> (<c>dominio.entidade.acao</c>).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Primeiro segmento do nome.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary><c>success</c> | <c>failure</c> | <c>unknown</c>.</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Motivo, quando informado.</summary>
    public string? Reason { get; init; }

    /// <summary>Nome da aplicação emissora.</summary>
    public string Application { get; init; } = string.Empty;

    /// <summary>Instante da emissão (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>TraceId W3C do span corrente na emissão, para cruzar com o APM.</summary>
    public string? TraceId { get; init; }

    /// <summary>SpanId W3C do span corrente na emissão.</summary>
    public string? SpanId { get; init; }

    /// <summary>Atributos de negócio mascarados, como texto.</summary>
    public Dictionary<string, string?> Attributes { get; init; } = new();

    /// <inheritdoc />
    public override string EventName => Name;

    /// <summary>Monta a mensagem a partir do registro normalizado do logger.</summary>
    public static BusinessEventMessage From(BusinessEventRecord record) => new() {
        Name = record.Name,
        Domain = record.Domain,
        Action = record.Action,
        Outcome = record.Outcome,
        Reason = record.Reason,
        Subject = record.Subject ?? string.Empty,
        Author = record.Author,
        Application = record.Application,
        OccurredAt = record.OccurredAt,
        Date = record.OccurredAt,
        CorrelationId = record.CorrelationId ?? Guid.NewGuid().ToString(),
        TraceId = record.TraceId,
        SpanId = record.SpanId,
        Attributes = new Dictionary<string, string?>(record.Attributes)
    };
}

/// <summary>
///     Publicação dos eventos de negócio auditáveis num destino de auditoria (seção
///     <c>Messaging:BusinessEventAudit</c>). Opt-in. Best-effort por desenho: a publicação sai por uma fila em
///     memória drenada em background, nunca no caminho da operação de negócio; fila cheia ou broker fora =
///     evento descartado com Warning e métrica <c>aedis.business.events.audit</c> (<c>result=dropped|failed</c>).
///     Quem precisa de garantia transacional usa outbox na própria aplicação.
/// </summary>
public sealed class BusinessEventAuditOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Messaging:BusinessEventAudit";

    /// <summary>Liga a publicação. Exige um <see cref="IMessageBrokerService" /> registrado.</summary>
    public bool Enabled { get; set; }

    /// <summary>Exchange/tópico de destino. Padrão <c>audit-events</c>.</summary>
    public string Exchange { get; set; } = "audit-events";

    /// <summary>Routing key. Padrão <c>business.events</c>.</summary>
    public string RoutingKey { get; set; } = "business.events";

    /// <summary>Capacidade da fila em memória entre a emissão e o publish. Padrão 10.000.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Quanto tempo o desligamento espera a fila esvaziar antes de descartar o restante. Padrão 10 s.</summary>
    public int DrainTimeoutSeconds { get; set; } = 10;
}

/// <summary>Métricas da trilha de auditoria de eventos de negócio.</summary>
internal static class BusinessEventAuditMetrics
{
    private static readonly Meter Meter = new("Aedis.Messaging.BusinessEventAudit");

    internal static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "aedis.business.events.audit", "{event}", "Eventos auditáveis por resultado da publicação (published, dropped, failed)");

    internal static void Record(string domain, string name, string result) =>
        Outcomes.Add(1, new TagList {
            { "application", ApplicationInfo.Name },
            { "event.domain", domain },
            { "event.name", name },
            { "result", result }
        });
}

/// <summary>
///     Fila em memória entre a emissão (síncrona, no handler) e o publish (assíncrono, em background). Nunca
///     bloqueia o produtor: cheia, descarta e avisa.
/// </summary>
public sealed class BusinessEventAuditQueue
{
    private readonly Channel<BusinessEventMessage> _channel;

    /// <summary>Cria a fila com a capacidade informada (mínimo 1).</summary>
    public BusinessEventAuditQueue(int capacity) {
        _channel = Channel.CreateBounded<BusinessEventMessage>(new BoundedChannelOptions(Math.Max(1, capacity)) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Leitor consumido pelo publisher.</summary>
    public ChannelReader<BusinessEventMessage> Reader => _channel.Reader;

    /// <summary>Enfileira; <c>false</c> quando a fila está cheia ou já foi fechada para o desligamento.</summary>
    public bool TryEnqueue(BusinessEventMessage message) => _channel.Writer.TryWrite(message);

    /// <summary>Fecha a escrita: o leitor drena o que restou e termina.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
///     <see cref="IBusinessEventSink" /> que enfileira os eventos auditáveis para o
///     <see cref="BusinessEventAuditPublisher" />. Eventos não auditáveis são ignorados.
/// </summary>
public sealed class BusinessEventAuditSink(BusinessEventAuditQueue queue, ILogger<BusinessEventAuditSink> logger) : IBusinessEventSink
{
    /// <inheritdoc />
    public void Observe(BusinessEventRecord record) {
        if (!record.Auditable) return;
        if (queue.TryEnqueue(BusinessEventMessage.From(record))) return;

        BusinessEventAuditMetrics.Record(record.Domain, record.Name, "dropped");
        logger.LogWarning("Fila de auditoria cheia ou fechada; evento de negócio {EventName} (sujeito {Subject}) não publicado.",
            record.Name, record.Subject);
    }
}

/// <summary>
///     Drena a <see cref="BusinessEventAuditQueue" /> e publica cada <see cref="BusinessEventMessage" /> em
///     <c>Exchange</c>/<c>RoutingKey</c> pelo <see cref="IMessageBrokerService" /> registrado. Falha de publish
///     não derruba nada: Error + métrica <c>result=failed</c> e segue. No desligamento fecha a fila e drena o
///     que restou até <c>DrainTimeoutSeconds</c>.
/// </summary>
public sealed class BusinessEventAuditPublisher(
    BusinessEventAuditQueue queue,
    BusinessEventAuditOptions options,
    IServiceProvider services,
    ILogger<BusinessEventAuditPublisher> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var broker = services.GetService<IMessageBrokerService>()
                     ?? throw new InvalidOperationException(
                         "Messaging:BusinessEventAudit:Enabled=true exige um IMessageBrokerService registrado (provider de broker do Aedis).");

        logger.LogDebug("Publisher de auditoria de eventos de negócio iniciado: {Exchange}/{RoutingKey}.", options.Exchange, options.RoutingKey);

        await foreach (var message in queue.Reader.ReadAllAsync(CancellationToken.None))
            await PublishAsync(broker, message);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken) {
        queue.Complete();
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0, options.DrainTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, drain.Token);
        await base.StopAsync(linked.Token);
    }

    private async Task PublishAsync(IMessageBrokerService broker, BusinessEventMessage message) {
        try {
            await broker.PublishAsync(options.Exchange, options.RoutingKey, message);
            BusinessEventAuditMetrics.Record(message.Domain, message.Name, "published");
        }
        catch (Exception ex) {
            BusinessEventAuditMetrics.Record(message.Domain, message.Name, "failed");
            logger.LogError(ex, "Evento de negócio {EventName} (sujeito {Subject}) não pôde ser publicado em {Exchange}/{RoutingKey}.",
                message.Name, message.Subject, options.Exchange, options.RoutingKey);
        }
    }
}
