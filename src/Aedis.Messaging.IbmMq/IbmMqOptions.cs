using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Opções do provider IBM MQ do Aedis. Lidas da seção <c>IBMMQ</c> da configuração.
/// </summary>
public sealed class IbmMqOptions
{
    public const string SectionName = "IBMMQ";

    [Required] public string QueueManager { get; set; } = null!;
    [Required] public string Channel { get; set; } = null!;
    [Required] public string ConnectionNameList { get; set; } = null!;
    [Required] public string UserId { get; set; } = null!;
    [Required] public string Password { get; set; } = null!;

    public int MaxSessions { get; set; } = 8;
    public int SessionTimeoutSeconds { get; set; } = 30;
    public int BackoffBaseMs { get; set; } = 500;
    public int BackoffMaxMs { get; set; } = 5000;

    /// <summary>Número de backouts (MQMD.BackoutCount) a partir do qual a mensagem vai para a fila de backout/DLQ.</summary>
    public int BackoutThreshold { get; set; } = 5;

    /// <summary>Fila de backout para mensagens que excedem o <see cref="BackoutThreshold" />.</summary>
    public string BackoutQueue { get; set; } = "DEV.BOQ";

    public bool UseIndentedJson { get; set; } = false;

    // Report / reply-to: o IBM MQ entrega confirmações (COA/COD) à fila informada no ReplyTo.
    public bool EnableReplyToQueue { get; set; } = false;
    public bool EnableReplyToQueueManager { get; set; } = false;

    /// <summary>Liga os report options do MQMD. Quais reports são pedidos é definido em <see cref="Reports" />.</summary>
    public bool EnableReports { get; set; } = true;

    public string? ReplyToReportQueueManager { get; set; }
    public string? ReplyToReportQueueAlias { get; set; }

    /// <summary>
    ///     Lista de ativação dos report options do MQMD (COA, COD, exceção, …) — substitui o conjunto
    ///     fixo que era embutido no código. Só tem efeito com <see cref="EnableReports" /> ligado.
    /// </summary>
    public MqReportOptions Reports { get; set; } = new();

    // Descritor padrão do MQMD para mensagens publicadas (defaults neutros; o específico do
    // consumidor — ex.: Request para fluxos com COA/COD — vira configuração).
    public MqMessageType MessageType { get; set; } = MqMessageType.Datagram;
    public MqPersistence Persistence { get; set; } = MqPersistence.Persistent;
    public MqMessageFormat Format { get; set; } = MqMessageFormat.None;

    /// <summary>Usa syncpoint (transação) no PUT e no GET. Padrão true (entrega transacional).</summary>
    public bool UseSyncpoint { get; set; } = true;

    public int ConsumerWaitIntervalMs { get; set; } = 5000; // espera do GET em WAIT mode (drain loop)
    public int ConsumerMaxRetries { get; set; } = 3;
    public int ConsumerBackoffMs { get; set; } = 1000;
    public int ConsumerHealthCheckIntervalMs { get; set; } = 60000;

    public bool EnableDeadLetterQueue { get; set; } = false;
    public string? DeadLetterQueueName { get; set; }
    public int MessageMaxRetries { get; set; } = 3;
    public bool RequireMessageAck { get; set; } = true;

    /// <summary>
    ///     CCSID (CodedCharSetId) do MQMD para as mensagens enviadas. O padrão 819 (ISO 8859-1, byte único)
    ///     representa qualquer byte 0x00–0xFF sem "Invalid character" — recomendado para payloads binários.
    ///     Evite CCSIDs multibyte (ex.: 1208/UTF-8) quando o conteúdo for binário, pois o cliente IBM MQ 9.x
    ///     pode reinterpretar os bytes.
    /// </summary>
    public int CodedCharSetId { get; set; } = 819;
}
