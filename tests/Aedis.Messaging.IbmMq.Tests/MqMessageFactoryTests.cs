using Aedis.Messaging.IbmMq;
using FluentAssertions;
using IBM.WMQ;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>
///     Garante que o plugin é dirigido por configuração e não por hardcode: a lista de ativação de
///     reports vira o bitmask MQRO_* esperado, os descritores do MQMD refletem as opções e o
///     CorrelationId é convertido para os 24 bytes do MQMD.
/// </summary>
public sealed class MqMessageFactoryTests
{
    [Fact]
    public void BuildReportFlags_sem_nada_ativo_e_MQRO_NONE() {
        MqMessageFactory.BuildReportFlags(new MqReportOptions()).Should().Be(MQC.MQRO_NONE);
    }

    [Fact]
    public void BuildReportFlags_ativa_apenas_o_que_foi_pedido() {
        var reports = new MqReportOptions { Coa = true, Cod = true, Exception = true, PassCorrelId = true };

        var flags = MqMessageFactory.BuildReportFlags(reports);

        flags.Should().Be(MQC.MQRO_COA | MQC.MQRO_COD | MQC.MQRO_EXCEPTION | MQC.MQRO_PASS_CORREL_ID);
    }

    [Fact]
    public void BuildReportFlags_nao_inclui_reports_desligados() {
        var flags = MqMessageFactory.BuildReportFlags(new MqReportOptions { Coa = true });

        (flags & MQC.MQRO_COD).Should().Be(0, "COD não foi ativado");
        (flags & MQC.MQRO_EXCEPTION).Should().Be(0, "Exception não foi ativado");
        (flags & MQC.MQRO_COA).Should().Be(MQC.MQRO_COA);
    }

    [Fact]
    public void BuildReportFlags_WithData_substitui_a_variante_simples() {
        var flags = MqMessageFactory.BuildReportFlags(new MqReportOptions { Coa = true, CoaWithData = true });

        (flags & MQC.MQRO_COA_WITH_DATA).Should().Be(MQC.MQRO_COA_WITH_DATA);
    }

    [Theory]
    [InlineData(MqMessageType.Datagram, MQC.MQMT_DATAGRAM)]
    [InlineData(MqMessageType.Request, MQC.MQMT_REQUEST)]
    [InlineData(MqMessageType.Reply, MQC.MQMT_REPLY)]
    [InlineData(MqMessageType.Report, MQC.MQMT_REPORT)]
    public void MapMessageType_traduz_corretamente(MqMessageType type, int expected) {
        MqMessageFactory.MapMessageType(type).Should().Be(expected);
    }

    [Fact]
    public void ToMq24_de_guid_preserva_os_16_bytes_em_24() {
        var guid = Guid.NewGuid();

        var result = MqMessageFactory.ToMq24(guid.ToString());

        result.Should().HaveCount(24);
        result.Take(16).Should().Equal(guid.ToByteArray());
        result.Skip(16).Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void ToMq24_de_string_nao_guid_usa_ate_24_bytes_utf8() {
        var result = MqMessageFactory.ToMq24("correlation-abc");

        result.Should().HaveCount(24);
        result.Take("correlation-abc".Length).Should().Equal(System.Text.Encoding.UTF8.GetBytes("correlation-abc"));
    }

    [Fact]
    public void ToMq24_vazio_vira_MQCI_NONE() {
        MqMessageFactory.ToMq24(string.Empty).Should().Equal(MQC.MQCI_NONE);
    }

    [Fact]
    public void BuildMqMessage_reflete_as_opcoes_no_MQMD() {
        var options = new IbmMqOptions {
            QueueManager = "QM", Channel = "CH", ConnectionNameList = "host(1414)", UserId = "u", Password = "p",
            MessageType = MqMessageType.Request,
            CodedCharSetId = 819,
            EnableReports = true,
            Reports = new MqReportOptions { Coa = true, Cod = true },
            EnableReplyToQueue = true,
            ReplyToReportQueueAlias = "REPLY.Q"
        };

        var message = MqMessageFactory.BuildMqMessage(options, Guid.NewGuid().ToString());

        message.MessageType.Should().Be(MQC.MQMT_REQUEST);
        message.CharacterSet.Should().Be(819);
        message.Report.Should().Be(MQC.MQRO_COA | MQC.MQRO_COD);
        message.ReplyToQueueName.Should().Be("REPLY.Q");
    }

    [Fact]
    public void BuildMqMessage_sem_EnableReports_nao_pede_reports() {
        var options = new IbmMqOptions {
            QueueManager = "QM", Channel = "CH", ConnectionNameList = "host(1414)", UserId = "u", Password = "p",
            EnableReports = false,
            Reports = new MqReportOptions { Coa = true, Cod = true }
        };

        var message = MqMessageFactory.BuildMqMessage(options, "abc");

        message.Report.Should().Be(MQC.MQRO_NONE, "EnableReports=false ignora a lista de ativação");
        message.MessageType.Should().Be(MQC.MQMT_DATAGRAM, "default neutro");
    }

    [Theory]
    [InlineData(MqPersistence.NotPersistent, MQC.MQPER_NOT_PERSISTENT)]
    [InlineData(MqPersistence.AsQueueDefinition, MQC.MQPER_PERSISTENCE_AS_Q_DEF)]
    [InlineData(MqPersistence.Persistent, MQC.MQPER_PERSISTENT)]
    public void MapPersistence_traduz_cada_modo(MqPersistence persistence, int expected) {
        MqMessageFactory.MapPersistence(persistence).Should().Be(expected);
    }

    [Fact]
    public void MapFormat_string_e_none_e_tipo_desconhecido_vira_datagram() {
        MqMessageFactory.MapFormat(MqMessageFormat.String).Should().Be(MQC.MQFMT_STRING);
        MqMessageFactory.MapFormat(MqMessageFormat.None).Should().Be(MQC.MQFMT_NONE);
        MqMessageFactory.MapMessageType((MqMessageType)99).Should().Be(MQC.MQMT_DATAGRAM);
        MqMessageFactory.ToMq24(null).Should().Equal((byte[])MQC.MQCI_NONE.Clone());
    }

    [Fact]
    public void ReplyToReportQueueManager_e_copiado_para_o_MQMD_e_descriptor_sem_reports_e_None() {
        var options = new IbmMqOptions {
            QueueManager = "QM1", Channel = "C", ConnectionNameList = "h(1414)", UserId = "u", Password = "p",
            EnableReplyToQueueManager = true, ReplyToReportQueueManager = "QMR"
        };

        var message = MqMessageFactory.BuildMqMessage(options, "abc");

        message.ReplyToQueueManagerName.Should().Be("QMR");
        new MqReportOptions().None.Should().BeTrue();
        new MqReportOptions { Coa = true }.None.Should().BeFalse();
    }

    [Fact]
    public void BuildReportFlags_cobre_cada_report_isoladamente() {
        MqMessageFactory.BuildReportFlags(new MqReportOptions { Cod = true }).Should().Be(MQC.MQRO_COD);
        MqMessageFactory.BuildReportFlags(new MqReportOptions { CodWithData = true, Cod = true }).Should().Be(MQC.MQRO_COD_WITH_DATA);
        MqMessageFactory.BuildReportFlags(new MqReportOptions { Expiration = true }).Should().Be(MQC.MQRO_EXPIRATION);
        MqMessageFactory.BuildReportFlags(new MqReportOptions { PassMsgId = true }).Should().Be(MQC.MQRO_PASS_MSG_ID);
        MqMessageFactory.BuildReportFlags(new MqReportOptions { PassCorrelId = true }).Should().Be(MQC.MQRO_PASS_CORREL_ID);
        MqMessageFactory.BuildReportFlags(new MqReportOptions { DiscardOnException = true, Exception = true })
            .Should().Be(MQC.MQRO_DISCARD_MSG | MQC.MQRO_EXCEPTION);
    }

    [Fact]
    public void Valores_desconhecidos_caem_no_default_e_guid_vazio_vira_MQCI_NONE() {
        MqMessageFactory.MapPersistence((MqPersistence)99).Should().Be(MQC.MQPER_PERSISTENT);
        MqMessageFactory.MapFormat((MqMessageFormat)99).Should().Be(MQC.MQFMT_NONE);
        MqMessageFactory.MapMessageType((MqMessageType)99).Should().Be(MQC.MQMT_DATAGRAM);
        MqMessageFactory.ToMq24(Guid.Empty.ToString()).Should().Equal(MQC.MQCI_NONE);
    }
}
