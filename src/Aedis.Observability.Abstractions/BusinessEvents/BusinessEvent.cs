namespace Aedis.Observability.Abstractions;

/// <summary>
///     Resultado de um evento de negócio — os valores de <c>event.outcome</c> das convenções ECS/OpenTelemetry.
///     Estado intermediário ("aceito, aguardando confirmação") não é outcome: é uma ação distinta
///     (<c>orders.order.accepted</c> versus <c>orders.order.confirmed</c>).
/// </summary>
public enum BusinessEventOutcome
{
    /// <summary>A ação aconteceu como esperado.</summary>
    Success,

    /// <summary>A ação falhou; <see cref="BusinessEvent.Reason" /> diz por quê.</summary>
    Failure,

    /// <summary>O resultado não é conhecido no momento da emissão.</summary>
    Unknown
}

/// <summary>
///     Fato de negócio estruturado: "aconteceu <see cref="Name" /> com o processo <see cref="Subject" />, com
///     resultado <see cref="Outcome" />". Emitido por <see cref="IBusinessEventLogger" /> como log
///     (<c>LogType=Event</c>, propriedades <c>event.*</c>), evento no span corrente e contador de baixa
///     cardinalidade — e, quando <see cref="Auditable" />, entregue aos <see cref="IBusinessEventSink" />
///     registrados (ex.: publicação numa trilha de auditoria).
/// </summary>
public sealed record BusinessEvent
{
    /// <summary>
    ///     <c>event.name</c> no formato <c>dominio.entidade.acao</c> — exatamente três segmentos, snake_case
    ///     (ex.: <c>orders.order.confirmed</c>). <see cref="Domain" /> e <see cref="Action" /> derivam dele.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Resultado do evento. Padrão <see cref="BusinessEventOutcome.Success" />.</summary>
    public BusinessEventOutcome Outcome { get; init; } = BusinessEventOutcome.Success;

    /// <summary><c>event.reason</c> — motivo legível da falha ou do desvio.</summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     <c>event.subject</c> — identificador do processo de negócio (id do pedido, da notificação). Vai para
    ///     log, span e auditoria; nunca para métrica.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    ///     <c>event.author</c> — quem pediu a ação (identificador técnico do usuário). Para ação do próprio
    ///     sistema (worker, job, retry automático) deixe nulo: o logger grava
    ///     <see cref="BusinessEventOptions.DefaultAuthor" />.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    ///     Marca o evento para a trilha de auditoria de longa duração: além de log/span/métrica, é entregue aos
    ///     sinks registrados. Use em fatos que alguém pode precisar reconstruir depois (cadastro, alteração,
    ///     cancelamento), não em rotina de alto volume. Padrão <c>false</c>.
    /// </summary>
    public bool Auditable { get; init; }

    /// <summary>
    ///     Correlação do evento. Quando nula, é resolvida do span de consumo corrente
    ///     (<c>messaging.message.conversation_id</c>) ou do TraceId ativo.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Atributos de negócio (snake_case, sem prefixo): <c>partner_id</c>, <c>attempt</c>. Sem payload e sem
    ///     dados pessoais — o que for sensível é mascarado pela política de redaction antes de sair.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Attributes { get; init; }

    /// <summary>Primeiro segmento do <see cref="Name" />.</summary>
    public string Domain => Name[..Name.IndexOf('.')];

    /// <summary>Último segmento do <see cref="Name" />.</summary>
    public string Action => Name[(Name.LastIndexOf('.') + 1)..];
}
