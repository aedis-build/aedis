namespace Aedis.Messaging.Abstractions;

/// <summary>
/// Campos do MQMD expostos após o IBM MQ receber uma mensagem. O <c>Feedback</c> traz os
/// report codes do IBM MQ (COA/COD/Expiry), úteis para mensagens de confirmação.
/// </summary>
public record MqMessageMetadata(
    string MsgId,             // BitConverter hex de MQMD.MessageId (24 bytes)
    string CorrelationIdHex,  // BitConverter hex de MQMD.CorrelationId (= CorrelId original via PASS_CORREL_ID)
    string AccountingToken,   // BitConverter hex de MQMD.AccountingToken (32 bytes)
    string ApplIdentityData,  // MQMD.ApplIdentityData trimmed (32 chars)
    string PutDate,           // MQMD.PutDateTime "yyyyMMdd HHmmss"
    int    Feedback,          // MQC.MQFB_COA=259, MQFB_COD=260, MQFB_EXPIRY=258, COA_WITH_DATA=275, COD_WITH_DATA=276
    int    MessageType        // MQC.MQMT_REPORT=4, MQMT_REQUEST=1
);
