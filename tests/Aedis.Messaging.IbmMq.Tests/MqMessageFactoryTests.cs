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
}
