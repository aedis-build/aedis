using Aedis.Messaging.IbmMq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.IbmMq.Tests;

/// <summary>
///     Teto duro de conexões do processo: reserva na alocação, falha com a aritmética na mensagem quando não
///     cabe, devolve ao parar — e o broker reserva o publisher na construção, sem conectar.
/// </summary>
public sealed class IbmMqConnectionBudgetTests
{
    [Fact]
    public void Reserva_e_devolve_dentro_do_teto() {
        var budget = new IbmMqConnectionBudget(6);

        budget.Reserve("publisher", 2).Should().Be(2);
        budget.Reserve("fila A", 3).Should().Be(3);
        budget.Granted.Should().Be(5);
        budget.Available.Should().Be(1);

        budget.Release(3);

        budget.Available.Should().Be(4);
    }

    [Fact]
    public void Pedido_que_nao_cabe_falha_com_a_conta_na_mensagem() {
        var budget = new IbmMqConnectionBudget(4);
        budget.Reserve("publisher", 2);

        var act = () => budget.Reserve("fila X", 3);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*fila X pediu 3*só há 2 livre(s) no teto de 4*já reservadas: 2*");
        budget.Granted.Should().Be(2, "uma reserva recusada não consome o teto");
    }

    [Fact]
    public void Pedido_zero_ou_negativo_reserva_ao_menos_uma() {
        var budget = new IbmMqConnectionBudget(2);

        budget.Reserve("fila", 0).Should().Be(1);
        budget.Granted.Should().Be(1);
    }

    [Fact]
    public void Broker_reserva_o_publisher_na_construcao_sem_conectar() {
        var options = Options.Create(new IbmMqOptions {
            QueueManager = "QM1",
            Channel = "DEV.APP.SVRCONN",
            ConnectionNameList = "localhost(1414)",
            UserId = "app",
            Password = "x",
            MaxConnections = 3,
            PublisherPoolSize = 2
        });

        var act = () => new IbmMqMessageBrokerService(options, NullLogger<IbmMqMessageBrokerService>.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Broker_recusa_teto_que_nao_sobra_conexao_para_consumers() {
        var options = Options.Create(new IbmMqOptions {
            QueueManager = "QM1",
            Channel = "DEV.APP.SVRCONN",
            ConnectionNameList = "localhost(1414)",
            UserId = "app",
            Password = "x",
            MaxConnections = 2,
            PublisherPoolSize = 2
        });

        var act = () => new IbmMqMessageBrokerService(options, NullLogger<IbmMqMessageBrokerService>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxConnections*PublisherPoolSize*");
    }
}
