namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Campos do MQMD expostos após o IBM MQ receber uma mensagem. O <c>Feedback</c> traz os
///     report codes do IBM MQ (COA/COD/Expiry), úteis para mensagens de confirmação.
/// </summary>
/// <param name="MsgId">Hex (BitConverter) de <c>MQMD.MessageId</c> — 24 bytes.</param>
/// <param name="CorrelationIdHex">
///     Hex (BitConverter) de <c>MQMD.CorrelationId</c>; equivale ao CorrelId original quando se usa
///     <c>PASS_CORREL_ID</c>.
/// </param>
/// <param name="AccountingToken">Hex (BitConverter) de <c>MQMD.AccountingToken</c> — 32 bytes.</param>
/// <param name="ApplIdentityData"><c>MQMD.ApplIdentityData</c> já trimado (32 chars).</param>
/// <param name="PutDate"><c>MQMD.PutDateTime</c> no formato <c>"yyyyMMdd HHmmss"</c>.</param>
/// <param name="Feedback">
///     Report code do IBM MQ: <c>MQFB_COA=259</c>, <c>MQFB_COD=260</c>, <c>MQFB_EXPIRY=258</c>,
///     <c>COA_WITH_DATA=275</c>, <c>COD_WITH_DATA=276</c>.
/// </param>
/// <param name="MessageType">Tipo da mensagem MQ: <c>MQMT_REPORT=4</c>, <c>MQMT_REQUEST=1</c>.</param>
public record MqMessageMetadata(
    string MsgId,
    string CorrelationIdHex,
    string AccountingToken,
    string ApplIdentityData,
    string PutDate,
    int    Feedback,
    int    MessageType
);
