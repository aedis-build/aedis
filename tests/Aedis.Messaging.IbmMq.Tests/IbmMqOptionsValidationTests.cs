using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>Validação das opções na construção do broker (sem conectar) e aviso de reports sem alias.</summary>
public sealed class IbmMqOptionsValidationTests
{
    private static IbmMqOptions Valid() => new() {
        QueueManager = "QM1", Channel = "DEV.APP.SVRCONN", ConnectionNameList = "localhost(1414)",
        UserId = "app", Password = "p", MaxConnections = 3, PublisherPoolSize = 1
    };

    private static Action Build(Action<IbmMqOptions> mutate) => () => {
        var options = Valid();
        mutate(options);
        _ = new IbmMqMessageBrokerService(Options.Create(options), NullLogger<IbmMqMessageBrokerService>.Instance);
    };

    [Fact]
    public void Campos_obrigatorios_e_dead_letter_sem_fila_falham() {
        Build(o => o.QueueManager = " ").Should().Throw<InvalidOperationException>().WithMessage("*QueueManager*");
        Build(o => o.ConnectionNameList = "").Should().Throw<InvalidOperationException>().WithMessage("*ConnectionNameList*");
        Build(o => o.Channel = "").Should().Throw<InvalidOperationException>().WithMessage("*Channel*");
        Build(o => o.EnableDeadLetterQueue = true).Should().Throw<InvalidOperationException>().WithMessage("*DeadLetterQueueName*");
    }

    [Fact]
    public void Reports_sem_alias_de_resposta_apenas_avisam() {
        Build(o => {
            o.EnableReports = true;
            o.Reports.Coa = true;
        }).Should().NotThrow();
    }
}
