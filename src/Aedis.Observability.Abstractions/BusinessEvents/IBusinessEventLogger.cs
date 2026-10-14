using Microsoft.Extensions.Logging;

namespace Aedis.Observability.Abstractions;

/// <summary>Emite eventos de negócio. Registrado por <c>AddAedisBusinessEvents</c> (os hosts do Aedis já chamam).</summary>
public interface IBusinessEventLogger
{
    /// <summary>Emite o evento: log, evento no span, métrica e sinks (quando auditável).</summary>
    void Emit(BusinessEvent evt);

    /// <summary>
    ///     Atalho: <c>Emit("orders.order.confirmed", subject: orderId, attributes: ("channel", "web"))</c>.
    /// </summary>
    void Emit(string name, BusinessEventOutcome outcome = BusinessEventOutcome.Success, string? subject = null,
        string? reason = null, params (string Key, object? Value)[] attributes);
}

/// <summary>
///     Destino adicional dos eventos de negócio, chamado após o log/span/métrica com o registro já normalizado
///     (autor resolvido, correlação resolvida, atributos mascarados e em texto). É o ponto onde a trilha de
///     auditoria se acopla sem o logger conhecer transporte algum. Nunca deve lançar: falhas são do sink.
/// </summary>
public interface IBusinessEventSink
{
    /// <summary>Recebe o registro de um evento emitido.</summary>
    void Observe(BusinessEventRecord record);
}

/// <summary>
///     Evento de negócio já normalizado para os sinks: valores resolvidos pelo logger e atributos como texto
///     mascarado pela política de redaction. Serializa igual em qualquer transporte.
/// </summary>
/// <param name="Name"><c>event.name</c>.</param>
/// <param name="Domain">Primeiro segmento do nome.</param>
/// <param name="Action">Último segmento do nome.</param>
/// <param name="Outcome"><c>success</c> | <c>failure</c> | <c>unknown</c>.</param>
/// <param name="Reason">Motivo, quando informado.</param>
/// <param name="Subject">Identificador do processo de negócio.</param>
/// <param name="Author">Quem pediu a ação; nunca nulo (padrão das opções para ação do sistema).</param>
/// <param name="Auditable">Marca de auditoria do evento.</param>
/// <param name="CorrelationId">Correlação resolvida.</param>
/// <param name="Application">Nome da aplicação emissora.</param>
/// <param name="OccurredAt">Instante da emissão (UTC).</param>
/// <param name="TraceId">TraceId W3C do span corrente, quando há.</param>
/// <param name="SpanId">SpanId W3C do span corrente, quando há.</param>
/// <param name="Attributes">Atributos mascarados, como texto.</param>
public sealed record BusinessEventRecord(
    string Name,
    string Domain,
    string Action,
    string Outcome,
    string? Reason,
    string? Subject,
    string Author,
    bool Auditable,
    string? CorrelationId,
    string Application,
    DateTimeOffset OccurredAt,
    string? TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, string?> Attributes);

/// <summary>Configuração dos eventos de negócio (seção <c>Observability:BusinessEvents</c>).</summary>
public sealed class BusinessEventOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Observability:BusinessEvents";

    /// <summary>Nível do log emitido. Padrão <see cref="Microsoft.Extensions.Logging.LogLevel.Information" /> — uma linha por fato, deliberada.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>Valor de <c>event.author</c> quando o evento não informa autor (ação do próprio sistema). Padrão <c>system</c>.</summary>
    public string DefaultAuthor { get; set; } = "system";

    /// <summary>Adiciona um <c>ActivityEvent</c> ao span corrente com os mesmos atributos. Padrão: sim.</summary>
    public bool EmitActivityEvent { get; set; } = true;

    /// <summary>Incrementa o contador <c>aedis.business.events</c> (domínio, nome, outcome). Padrão: sim.</summary>
    public bool EmitMetric { get; set; } = true;
}
