using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Aedis.Architecture.Tests;

/// <summary>
///     Testes de conformidade arquitetural — a regra de dependência do Aedis,
///     verificada em CI por serviço/biblioteca. Ver ARCHITECTURE.md e MIGRATION.md.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Dominio_nao_depende_de_AspNetCore() {
        var result = Types.InAssembly(typeof(Aedis.Domain.Specifications.Abstractions.ISpecification<>).Assembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("o domínio (Aedis.Domain) não pode depender de ASP.NET Core");
    }

    [Fact]
    public void Dominio_nao_depende_de_implementacoes_de_provider() {
        var result = Types.InAssembly(typeof(Aedis.Domain.Specifications.Abstractions.ISpecification<>).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Npgsql", "StackExchange.Redis", "RabbitMQ.Client", "Amazon", "IBM.WMQ")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("o domínio não pode referenciar pacotes de implementação concreta");
    }

    [Fact]
    public void Messaging_Abstractions_nao_depende_de_provider() {
        var result = Types.InAssembly(typeof(Aedis.Messaging.Abstractions.IRawMessage).Assembly)
            .Should()
            .NotHaveDependencyOnAny("IBM.WMQ", "RabbitMQ.Client", "Amazon", "Azure.Messaging.ServiceBus")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Messaging.Abstractions deve permanecer agnóstica: a semântica de broker desce para o pacote do provider");
    }

    [Fact]
    public void Messaging_neutro_nao_depende_de_provider_nem_de_AspNetCore() {
        var result = Types.InAssembly(typeof(Aedis.Messaging.ScopedMessageHandler<>).Assembly)
            .Should()
            .NotHaveDependencyOnAny("IBM.WMQ", "RabbitMQ.Client", "Amazon", "Azure.Messaging.ServiceBus", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Messaging é a implementação neutra sobre IMessageBrokerService: nenhum SDK de broker nem ASP.NET Core");
    }

    [Fact]
    public void Signing_Abstractions_so_depende_da_BCL_e_das_excecoes() {
        var result = Types.InAssembly(typeof(Aedis.Signing.Abstractions.ISigningKeyProvider).Assembly)
            .Should()
            .NotHaveDependencyOnAny("NSign", "Amazon", "Azure", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Signing.Abstractions é só contrato: a biblioteca de assinatura e os SDKs de cofre ficam nos pacotes de implementação");
    }

    [Fact]
    public void Signing_neutro_nao_depende_de_cofre_nem_de_AspNetCore() {
        var result = Types.InAssembly(typeof(Aedis.Signing.HttpMessageSigner).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Amazon", "Azure", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Signing fala com o cofre só pela costura ISignatureProvider/ISigningKeyProvider dos pacotes de provider");
    }
}
