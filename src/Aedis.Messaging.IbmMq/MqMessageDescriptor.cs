namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Lista de ativação dos <em>report options</em> do MQMD (MQRO_*). Substitui o conjunto fixo que a
///     implementação original embutia (COA|COD|EXCEPTION|PASS_CORREL_ID): cada confirmação/relatório do
///     IBM MQ vira uma chave ligável por configuração. Só tem efeito quando
///     <see cref="IbmMqOptions.EnableReports" /> está ativo.
/// </summary>
public sealed class MqReportOptions
{
    /// <summary>Confirmation On Arrival — o QM confirma quando a mensagem chega à fila de destino.</summary>
    public bool Coa { get; set; }

    /// <summary>Confirmation On Delivery — o QM confirma quando a mensagem é lida pela aplicação destino.</summary>
    public bool Cod { get; set; }

    /// <summary>Report de exceção (mensagem não entregue).</summary>
    public bool Exception { get; set; }

    /// <summary>Report de expiração (mensagem expirou antes de ser consumida).</summary>
    public bool Expiration { get; set; }

    /// <summary>Propaga o CorrelationId original nos reports (MQRO_PASS_CORREL_ID).</summary>
    public bool PassCorrelId { get; set; }

    /// <summary>Propaga o MessageId original nos reports (MQRO_PASS_MSG_ID).</summary>
    public bool PassMsgId { get; set; }

    /// <summary>Inclui os primeiros 100 bytes do payload nos reports COA (MQRO_COA_WITH_DATA).</summary>
    public bool CoaWithData { get; set; }

    /// <summary>Inclui os primeiros 100 bytes do payload nos reports COD (MQRO_COD_WITH_DATA).</summary>
    public bool CodWithData { get; set; }

    /// <summary>Descarta a mensagem original ao gerar um report de exceção (MQRO_DISCARD_MSG).</summary>
    public bool DiscardOnException { get; set; }

    /// <summary>True quando nenhuma chave de report está ativa.</summary>
    public bool None =>
        !(Coa || Cod || Exception || Expiration || PassCorrelId || PassMsgId || CoaWithData || CodWithData);
}

/// <summary>Tipo da mensagem no MQMD (MQMT_*). Determina a semântica de request/reply do IBM MQ.</summary>
public enum MqMessageType
{
    /// <summary>Datagrama — envio sem resposta esperada (padrão neutro).</summary>
    Datagram,

    /// <summary>Request — espera uma resposta na fila de ReplyTo (usado com reports COA/COD).</summary>
    Request,

    /// <summary>Reply — resposta a um request.</summary>
    Reply,

    /// <summary>Report — relatório (COA/COD/exceção).</summary>
    Report
}

/// <summary>Persistência da mensagem no MQMD (MQPER_*).</summary>
public enum MqPersistence
{
    /// <summary>Persistente — sobrevive a restart do QM.</summary>
    Persistent,

    /// <summary>Não persistente — pode ser perdida em restart.</summary>
    NotPersistent,

    /// <summary>Herda a definição da fila.</summary>
    AsQueueDefinition
}

/// <summary>Formato do corpo da mensagem no MQMD (MQFMT_*).</summary>
public enum MqMessageFormat
{
    /// <summary>Sem formato — bytes brutos (recomendado para payloads binários).</summary>
    None,

    /// <summary>String — texto sujeito a conversão de CCSID pelo QM.</summary>
    String
}
