using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Opções do provider IBM MQ do Aedis, lidas da seção <c>IBMMQ</c>. Toda conexão do cliente é um canal
///     no queue manager, e o QM tem um teto global de canais compartilhado por todas as réplicas — por isso o
///     provider trabalha com um <strong>teto duro por processo</strong> (<see cref="MaxConnections" />) que
///     precisa comportar <c>Σ(concorrência de cada fila) + PublisherPoolSize</c>. Configuração que não cabe
///     derruba o host na subida, com a conta na mensagem, em vez de degradar em silêncio.
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

    /// <summary>
    ///     Teto duro de conexões (canais) que este processo abre no queue manager, contando o pool do
    ///     publisher e os workers de todos os consumers. A reserva acontece na alocação (publisher ao subir,
    ///     cada fila ao assinar); quem não cabe falha na subida. Padrão 8.
    /// </summary>
    public int MaxConnections { get; set; } = 8;

    /// <summary>
    ///     Tamanho do pool de conexões usado para publicar. Cada publicação empresta uma conexão, faz
    ///     <c>Put + Commit</c> isolado e a devolve. Entra no teto de <see cref="MaxConnections" />. Padrão 2.
    /// </summary>
    public int PublisherPoolSize { get; set; } = 2;

    /// <summary>
    ///     Número padrão de workers (conexões dedicadas) por fila consumida, quando a fila não aparece em
    ///     <see cref="QueueConcurrency" />. Cada worker faz <c>Get → handler → Commit</c> na própria conexão,
    ///     então workers paralelos processam a fila concorrentemente (sem preservar ordem). Padrão 1.
    /// </summary>
    public int ConsumerConcurrency { get; set; } = 1;

    /// <summary>
    ///     Concorrência por fila (nome → nº de workers), sobrepondo <see cref="ConsumerConcurrency" /> só para
    ///     as filas listadas — ex.: <c>IBMMQ:QueueConcurrency:FILA.ALTA = 10</c>.
    /// </summary>
    public Dictionary<string, int> QueueConcurrency { get; set; } = new();

    /// <summary>Intervalo de espera do GET em modo WAIT, em milissegundos (o loop alterna WAIT/DRAIN). Padrão 5000.</summary>
    public int ConsumerWaitIntervalMs { get; set; } = 5000;

    /// <summary>Espera do worker após um erro MQ não crítico antes de voltar ao GET, em milissegundos. Padrão 1000.</summary>
    public int ConsumerBackoffMs { get; set; } = 1000;

    /// <summary>Intervalo entre verificações de saúde do consumer (religa workers mortos), em milissegundos. Padrão 60000.</summary>
    public int ConsumerHealthCheckIntervalMs { get; set; } = 60000;

    /// <summary>Usa syncpoint (transação) no PUT e no GET. Padrão true: a mensagem só sai da fila após o handler concluir.</summary>
    public bool UseSyncpoint { get; set; } = true;

    /// <summary>
    ///     Desvio opcional para dead-letter: mensagens cujo <c>MQMD.BackoutCount</c> atinge
    ///     <see cref="BackoutThreshold" /> são movidas para <see cref="DeadLetterQueueName" /> e confirmadas.
    ///     Desligado por padrão — a aplicação decide o destino de mensagens problemáticas no handler.
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; }

    /// <summary>Fila de dead-letter; obrigatória quando <see cref="EnableDeadLetterQueue" /> está ligado.</summary>
    public string? DeadLetterQueueName { get; set; }

    /// <summary>Número de backouts a partir do qual a mensagem vai para a dead-letter. Padrão 5.</summary>
    public int BackoutThreshold { get; set; } = 5;

    /// <summary>Preenche o <c>ReplyToQueueName</c> do MQMD com <see cref="ReplyToReportQueueAlias" />.</summary>
    public bool EnableReplyToQueue { get; set; }

    /// <summary>Preenche o <c>ReplyToQueueManagerName</c> do MQMD com <see cref="ReplyToReportQueueManager" />.</summary>
    public bool EnableReplyToQueueManager { get; set; }

    /// <summary>Liga os report options do MQMD definidos em <see cref="Reports" />. Desligado por padrão.</summary>
    public bool EnableReports { get; set; }

    /// <summary>Queue Manager de destino das confirmações/reports (usado com <see cref="EnableReplyToQueueManager" />).</summary>
    public string? ReplyToReportQueueManager { get; set; }

    /// <summary>Fila (alias) de destino das confirmações/reports (usado com <see cref="EnableReplyToQueue" />).</summary>
    public string? ReplyToReportQueueAlias { get; set; }

    /// <summary>Lista de ativação dos report options do MQMD (COA, COD, exceção, …). Só tem efeito com <see cref="EnableReports" />.</summary>
    public MqReportOptions Reports { get; set; } = new();

    /// <summary>Tipo do MQMD das mensagens publicadas. Padrão <see cref="MqMessageType.Datagram" />.</summary>
    public MqMessageType MessageType { get; set; } = MqMessageType.Datagram;

    /// <summary>Persistência do MQMD das mensagens publicadas. Padrão <see cref="MqPersistence.Persistent" />.</summary>
    public MqPersistence Persistence { get; set; } = MqPersistence.Persistent;

    /// <summary>Formato do corpo no MQMD das mensagens publicadas. Padrão <see cref="MqMessageFormat.None" /> (bytes brutos).</summary>
    public MqMessageFormat Format { get; set; } = MqMessageFormat.None;

    /// <summary>
    ///     CCSID (CodedCharSetId) do MQMD das mensagens enviadas. Padrão 1208 (UTF-8), adequado a JSON e texto.
    ///     Para payloads binários com conversão de CCSID no caminho, prefira um CCSID de byte único (ex.: 819).
    /// </summary>
    public int CodedCharSetId { get; set; } = 1208;
}
