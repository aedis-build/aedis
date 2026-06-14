using NetArchTest.Rules;
using Xunit;

namespace Aedis.Architecture.Tests;

/// <summary>
/// Testes de conformidade arquitetural — a regra de dependência do Aedis,
/// verificada em CI por serviço/biblioteca. Ver ARCHITECTURE.md e MIGRATION.md.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Dominio_nao_depende_de_AspNetCore()
    {
        var result = Types.InAssembly(typeof(Aedis.Domain.Specifications.Abstractions.ISpecification<>).Assembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domínio (Aedis.Domain) não pode depender de ASP.NET Core.");
    }

    [Fact]
    public void Dominio_nao_depende_de_implementacoes_de_provider()
    {
        var result = Types.InAssembly(typeof(Aedis.Domain.Specifications.Abstractions.ISpecification<>).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Npgsql", "StackExchange.Redis", "RabbitMQ.Client",
                "Amazon", "IBM.WMQ")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domínio não pode referenciar pacotes de implementação concreta.");
    }
}
