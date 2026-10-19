using System.Reflection;
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

    [Fact]
    public void Payments_Abstractions_nao_depende_do_SDK_do_provedor() {
        var result = Types.InAssembly(typeof(Aedis.Payments.Abstractions.IPaymentGateway).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Stripe", "System.Net.Http", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Payments.Abstractions é só contrato: o SDK do provedor de pagamento fica no pacote de implementação");
    }

    [Fact]
    public void ExchangeRates_Abstractions_nao_depende_de_HTTP() {
        var result = Types.InAssembly(typeof(Aedis.ExchangeRates.Abstractions.IExchangeRates).Assembly)
            .Should()
            .NotHaveDependencyOnAny("System.Net.Http", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.ExchangeRates.Abstractions é só contrato: a consulta à fonte da cotação fica no pacote de implementação");
    }

    [Fact]
    public void AI_Abstractions_nao_depende_do_SDK_do_provedor() {
        var result = Types.InAssembly(typeof(Aedis.AI.Abstractions.IAgentEngine).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Anthropic", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.AI.Abstractions é só contrato: o SDK do provedor de agentes fica no pacote de implementação");
    }

    [Fact]
    public void Tenancy_Abstractions_nao_depende_de_AspNetCore_nem_de_provider() {
        var result = Types.InAssembly(typeof(Aedis.Tenancy.Abstractions.ICurrentTenant).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.AspNetCore", "Npgsql", "Dapper", "Aedis.Security.Keycloak")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Tenancy.Abstractions é só contrato: resolução por token, middleware e banco ficam nos pacotes de implementação");
    }

    [Fact]
    public void Hypermedia_Abstractions_nao_depende_de_AspNetCore() {
        var result = Types.InAssembly(typeof(Aedis.Hypermedia.Abstractions.IHypermedia).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.Extensions.DependencyInjection", "Microsoft.OpenApi")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.Hypermedia.Abstractions é só contrato e envelope HAL: LinkGenerator, filtros e DI ficam em Aedis.Hypermedia.AspNetCore");
    }

    [Fact]
    public void OpenApi_Abstractions_nao_depende_do_gerador_nem_de_AspNetCore() {
        var result = Types.InAssembly(typeof(Aedis.OpenApi.Abstractions.AedisOpenApiOptions).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.OpenApi", "Scalar", "Swashbuckle")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Aedis.OpenApi.Abstractions é só opções e marcadores: o gerador e a UI ficam em Aedis.OpenApi e Aedis.OpenApi.Scalar");
    }

    /// <summary>
    ///     Pacotes <c>*.Abstractions</c> publicam contratos e DTOs; quem implementa um contrato do próprio pacote é
    ///     implementação e pertence a um pacote neutro ou ao projeto de testes. Messaging e Storage ainda carregam
    ///     serializadores e serviços legados e entram quando forem limpos.
    /// </summary>
    [Theory]
    [InlineData(typeof(Aedis.Payments.Abstractions.IPaymentGateway))]
    [InlineData(typeof(Aedis.ExchangeRates.Abstractions.IExchangeRates))]
    [InlineData(typeof(Aedis.AI.Abstractions.IAgentEngine))]
    [InlineData(typeof(Aedis.Signing.Abstractions.ISigningKeyProvider))]
    [InlineData(typeof(Aedis.Tenancy.Abstractions.ICurrentTenant))]
    [InlineData(typeof(Aedis.OpenApi.Abstractions.AedisOpenApiOptions))]
    public void Abstractions_nao_implementa_os_proprios_contratos(Type marker) {
        var assembly = marker.Assembly;
        var implementations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsSubclassOf(typeof(Exception)))
            .Where(t => t.GetInterfaces().Any(i => i.Assembly == assembly))
            .Select(t => t.FullName)
            .ToList();

        implementations.Should().BeEmpty(
            $"{assembly.GetName().Name} é só contrato: implementações vão para um pacote neutro ou para os testes");
    }
}
