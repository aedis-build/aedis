using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Opções do provider IBM MQ do Aedis. Lidas da seção <c>IBMMQ</c> da configuração.
/// </summary>
public sealed class IbmMqOptions
{
    /// <summary>Nome da seção de configuração de onde as opções são lidas (<c>IBMMQ</c>).</summary>
    public const string SectionName = "IBMMQ";

    /// <summary>Nome do Queue Manager ao qual conectar.</summary>
    [Required] public string QueueManager { get; set; } = null!;

    /// <summary>Canal SVRCONN usado na conexão cliente.</summary>
    [Required] public string Channel { get; set; } = null!;

    /// <summary>Lista de endpoints de conexão no formato <c>host(porta)</c> (suporta múltiplos, separados por vírgula).</summary>
    [Required] public string ConnectionNameList { get; set; } = null!;

    /// <summary>Usuário usado na autenticação MQCSP.</summary>
    [Required] public string UserId { get; set; } = null!;

    /// <summary>Senha usada na autenticação MQCSP.</summary>
    [Required] public string Password { get; set; } = null!;

    /// <summary>Número máximo de sessões mantidas pelo provider.</summary>
    public int MaxSessions { get; set; } = 8;

    /// <summary>Timeout de sessão, em segundos.</summary>
    public int SessionTimeoutSeconds { get; set; } = 30;

    /// <summary>Base do backoff exponencial de reconexão, em milissegundos.</summary>
    public int BackoffBaseMs { get; set; } = 500;

    /// <summary>Teto do backoff exponencial de reconexão, em milissegundos.</summary>
    public int BackoffMaxMs { get; set; } = 5000;

    /// <summary>Número de backouts (MQMD.BackoutCount) a partir do qual a mensagem vai para a fila de backout/DLQ.</summary>
    public int BackoutThreshold { get; set; } = 5;

    /// <summary>Fila de backout para mensagens que excedem o <see cref="BackoutThreshold" />.</summary>
    public string BackoutQueue { get; set; } = "DEV.BOQ";

    /// <summary>Serializa o JSON com indentação (legibilidade) em vez de compacto. Padrão false.</summary>
    public bool UseIndentedJson { get; set; } = false;

    /// <summary>
    ///     Preenche o <c>ReplyToQueueName</c> do MQMD com <see cref="ReplyToReportQueueAlias" />. O IBM MQ
    ///     entrega as confirmações (COA/COD) à fila informada no ReplyTo.
    /// </summary>
    public bool EnableReplyToQueue { get; set; } = false;

    /// <summary>Preenche o <c>ReplyToQueueManagerName</c> do MQMD com <see cref="ReplyToReportQueueManager" />.</summary>
    public bool EnableReplyToQueueManager { get; set; } = false;

    /// <summary>Liga os report options do MQMD. Quais reports são pedidos é definido em <see cref="Reports" />.</summary>
    public bool EnableReports { get; set; } = true;

    /// <summary>Queue Manager de destino das confirmações/reports (usado com <see cref="EnableReplyToQueueManager" />).</summary>
    public string? ReplyToReportQueueManager { get; set; }

    /// <summary>Fila (alias) de destino das confirmações/reports (usado com <see cref="EnableReplyToQueue" />).</summary>
    public string? ReplyToReportQueueAlias { get; set; }

    /// <summary>
    ///     Lista de ativação dos report options do MQMD (COA, COD, exceção, …) — substitui o conjunto
    ///     fixo que era embutido no código. Só tem efeito com <see cref="EnableReports" /> ligado.
    /// </summary>
    public MqReportOptions Reports { get; set; } = new();

    /// <summary>
    ///     Tipo do MQMD das mensagens publicadas. O default é <see cref="MqMessageType.Datagram" /> (neutro);
    ///     fluxos com COA/COD costumam usar <see cref="MqMessageType.Request" />, definido por configuração.
    /// </summary>
    public MqMessageType MessageType { get; set; } = MqMessageType.Datagram;

    /// <summary>Persistência do MQMD das mensagens publicadas. Padrão <see cref="MqPersistence.Persistent" />.</summary>
    public MqPersistence Persistence { get; set; } = MqPersistence.Persistent;

    /// <summary>Formato do corpo no MQMD das mensagens publicadas. Padrão <see cref="MqMessageFormat.None" /> (bytes brutos).</summary>
    public MqMessageFormat Format { get; set; } = MqMessageFormat.None;

    /// <summary>Usa syncpoint (transação) no PUT e no GET. Padrão true (entrega transacional).</summary>
    public bool UseSyncpoint { get; set; } = true;

    /// <summary>Intervalo de espera do GET em modo WAIT, em milissegundos (o loop alterna WAIT/DRAIN).</summary>
    public int ConsumerWaitIntervalMs { get; set; } = 5000;

    /// <summary>Número máximo de tentativas do consumidor antes de desistir.</summary>
    public int ConsumerMaxRetries { get; set; } = 3;

    /// <summary>Backoff entre tentativas do consumidor após um erro MQ não crítico, em milissegundos.</summary>
    public int ConsumerBackoffMs { get; set; } = 1000;

    /// <summary>Intervalo entre verificações de saúde do consumidor, em milissegundos.</summary>
    public int ConsumerHealthCheckIntervalMs { get; set; } = 60000;

    /// <summary>Habilita o roteamento de mensagens com falha repetida para a fila de backout/DLQ.</summary>
    public bool EnableDeadLetterQueue { get; set; } = false;

    /// <summary>Fila de dead-letter; quando nula, usa <see cref="BackoutQueue" />.</summary>
    public string? DeadLetterQueueName { get; set; }

    /// <summary>Número máximo de reentregas de uma mensagem antes de considerá-la falha permanente.</summary>
    public int MessageMaxRetries { get; set; } = 3;

    /// <summary>Exige ACK explícito da mensagem (consumo transacional). Padrão true.</summary>
    public bool RequireMessageAck { get; set; } = true;

    /// <summary>
    ///     CCSID (CodedCharSetId) do MQMD para as mensagens enviadas. O padrão 819 (ISO 8859-1, byte único)
    ///     representa qualquer byte 0x00–0xFF sem "Invalid character" — recomendado para payloads binários.
    ///     Evite CCSIDs multibyte (ex.: 1208/UTF-8) quando o conteúdo for binário, pois o cliente IBM MQ 9.x
    ///     pode reinterpretar os bytes.
    /// </summary>
    public int CodedCharSetId { get; set; } = 819;
}
