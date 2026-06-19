using System.Text;
using Aedis.Messaging.Abstractions;
using IBM.WMQ;

namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Monta o MQMD das mensagens publicadas a partir das <see cref="IbmMqOptions" /> — traduzindo a
///     lista de ativação <see cref="MqReportOptions" /> e os descritores (tipo, persistência, formato)
///     em valores MQC. Tudo o que era hardcoded vira aqui o reflexo das opções, mantendo o plugin
///     agnóstico. Os métodos são puros (não dependem de conexão) e por isso unit-testáveis.
/// </summary>
internal static class MqMessageFactory
{
    private const int CorrelIdLength = 24;

    /// <summary>Cria o MQMessage com o cabeçalho (MQMD) configurado pelas opções, sem corpo.</summary>
    internal static MQMessage BuildMqMessage(IbmMqOptions options, string? correlationId) {
        var message = new MQMessage {
            MessageType = MapMessageType(options.MessageType),
            Persistence = MapPersistence(options.Persistence),
            Encoding = MQC.MQENC_NATIVE,
            CharacterSet = options.CodedCharSetId,
            Format = MapFormat(options.Format),
            CorrelationId = ToMq24(correlationId)
        };

        if (options.EnableReplyToQueue && !string.IsNullOrEmpty(options.ReplyToReportQueueAlias))
            message.ReplyToQueueName = options.ReplyToReportQueueAlias;

        if (options.EnableReplyToQueueManager && !string.IsNullOrEmpty(options.ReplyToReportQueueManager))
            message.ReplyToQueueManagerName = options.ReplyToReportQueueManager;

        if (options.EnableReports) {
            var report = BuildReportFlags(options.Reports);
            if (report != MQC.MQRO_NONE)
                message.Report = report;
        }

        return message;
    }

    /// <summary>Lê os campos do MQMD de uma mensagem recebida para o registro neutro de metadados.</summary>
    internal static MqMessageMetadata ReadMetadata(MQMessage message) => new(
        MsgId: Convert.ToHexString(message.MessageId),
        CorrelationIdHex: Convert.ToHexString(message.CorrelationId),
        AccountingToken: message.AccountingToken,
        ApplIdentityData: string.Empty,
        PutDate: message.PutDateTime.ToString("yyyyMMdd HHmmss"),
        Feedback: message.Feedback,
        MessageType: message.MessageType);

    /// <summary>
    ///     Traduz a lista de ativação em um bitmask MQRO_* (0/MQRO_NONE quando nada está ativo). A variante
    ///     <c>WithData</c> já implica a simples (COA_WITH_DATA cobre COA; idem COD), então só uma é pedida
    ///     para não duplicar o report.
    /// </summary>
    internal static int BuildReportFlags(MqReportOptions reports) {
        var flags = MQC.MQRO_NONE;

        if (reports.CoaWithData) flags |= MQC.MQRO_COA_WITH_DATA;
        else if (reports.Coa) flags |= MQC.MQRO_COA;

        if (reports.CodWithData) flags |= MQC.MQRO_COD_WITH_DATA;
        else if (reports.Cod) flags |= MQC.MQRO_COD;

        if (reports.Exception) flags |= MQC.MQRO_EXCEPTION;
        if (reports.Expiration) flags |= MQC.MQRO_EXPIRATION;
        if (reports.PassCorrelId) flags |= MQC.MQRO_PASS_CORREL_ID;
        if (reports.PassMsgId) flags |= MQC.MQRO_PASS_MSG_ID;
        if (reports.DiscardOnException) flags |= MQC.MQRO_DISCARD_MSG;

        return flags;
    }

    internal static int MapMessageType(MqMessageType type) => type switch {
        MqMessageType.Datagram => MQC.MQMT_DATAGRAM,
        MqMessageType.Request => MQC.MQMT_REQUEST,
        MqMessageType.Reply => MQC.MQMT_REPLY,
        MqMessageType.Report => MQC.MQMT_REPORT,
        _ => MQC.MQMT_DATAGRAM
    };

    internal static int MapPersistence(MqPersistence persistence) => persistence switch {
        MqPersistence.Persistent => MQC.MQPER_PERSISTENT,
        MqPersistence.NotPersistent => MQC.MQPER_NOT_PERSISTENT,
        MqPersistence.AsQueueDefinition => MQC.MQPER_PERSISTENCE_AS_Q_DEF,
        _ => MQC.MQPER_PERSISTENT
    };

    internal static string MapFormat(MqMessageFormat format) => format switch {
        MqMessageFormat.None => MQC.MQFMT_NONE,
        MqMessageFormat.String => MQC.MQFMT_STRING,
        _ => MQC.MQFMT_NONE
    };

    /// <summary>
    ///     Converte o CorrelationId para os 24 bytes do MQMD: GUID quando possível (preserva o valor
    ///     binário), senão os primeiros 24 bytes UTF-8; vazio vira MQCI_NONE. Garante que
    ///     MQRO_PASS_CORREL_ID propague o CorrelationId real nos reports.
    /// </summary>
    internal static byte[] ToMq24(string? correlationId) {
        if (string.IsNullOrEmpty(correlationId))
            return (byte[])MQC.MQCI_NONE.Clone();

        if (Guid.TryParse(correlationId, out var guid))
            return GuidToMq24(guid);

        var src = Encoding.UTF8.GetBytes(correlationId);
        var dst = new byte[CorrelIdLength];
        Buffer.BlockCopy(src, 0, dst, 0, Math.Min(src.Length, CorrelIdLength));
        return dst;
    }

    private static byte[] GuidToMq24(Guid guid) {
        if (guid == Guid.Empty)
            return (byte[])MQC.MQCI_NONE.Clone();

        var src = guid.ToByteArray();
        var dst = new byte[CorrelIdLength];
        Buffer.BlockCopy(src, 0, dst, 0, Math.Min(src.Length, CorrelIdLength));
        return dst;
    }
}
