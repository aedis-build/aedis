using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aedis.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Aedis.Analyzers.Tests;

/// <summary>
///     Cobre a regra de dependência AEDIS0001: um assembly de domínio/aplicação que <strong>usa</strong> um
///     tipo de uma implementação concreta do Aedis é sinalizado; usar contratos é permitido; camadas não
///     restritas (ex.: infraestrutura) ficam livres; e dependência apenas referenciada (não usada) não gera
///     falso-positivo.
/// </summary>
public sealed class DependencyRuleAnalyzerTests {
    private static readonly MetadataReference Corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly CSharpCompilationOptions Library = new(OutputKind.DynamicallyLinkedLibrary);

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(string assemblyName, params string[] usedAedisAssemblies) {
        var references = new List<MetadataReference> { Corlib };
        var fields = new StringBuilder();
        for (var i = 0; i < usedAedisAssemblies.Length; i++) {
            references.Add(FakeAssembly(usedAedisAssemblies[i]));
            fields.AppendLine($"    private {usedAedisAssemblies[i]}.Marker _f{i} = null!;");
        }

        var source = $"public class Sample {{\n{fields}}}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            Library);

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DependencyRuleAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(diagnostic => diagnostic.Id == DependencyRuleAnalyzer.DiagnosticId).ToImmutableArray();
    }

    private static MetadataReference FakeAssembly(string name) {
        var source = $"namespace {name} {{ public class Marker {{ }} }}";
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, new[] { Corlib }, Library);
        return compilation.ToMetadataReference();
    }

    [Fact]
    public async Task Dominio_usando_implementacao_reporta() {
        var diagnostics = await RunAsync("Acme.Shop.Domain", "Aedis.Database.Postgres");

        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should().Contain("Aedis.Database.Postgres");
    }

    [Fact]
    public async Task Application_usando_implementacao_reporta() {
        var diagnostics = await RunAsync("Acme.Shop.Application", "Aedis.Cache.Redis");

        diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task Contratos_permitidos_nao_reportam() {
        var diagnostics = await RunAsync(
            "Acme.Shop.Domain",
            "Aedis.Core", "Aedis.Domain", "Aedis.Exceptions", "Aedis.Commands", "Aedis.Database.Abstractions");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Camada_de_infraestrutura_nao_e_restrita() {
        var diagnostics = await RunAsync("Acme.Shop.Infrastructure", "Aedis.Database.Postgres");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Cada_implementacao_usada_gera_um_diagnostico() {
        var diagnostics = await RunAsync("Acme.Shop.Domain", "Aedis.Database.Postgres", "Aedis.Messaging.RabbitMq");

        diagnostics.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dependencia_referenciada_mas_nao_usada_nao_reporta() {
        var compilation = CSharpCompilation.Create(
            "Acme.Shop.Domain",
            new[] { CSharpSyntaxTree.ParseText("public class Sample { }") },
            new[] { Corlib, FakeAssembly("Aedis.Database.Postgres") },
            Library);

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DependencyRuleAnalyzer()));
        var diagnostics = (await withAnalyzers.GetAnalyzerDiagnosticsAsync())
            .Where(diagnostic => diagnostic.Id == DependencyRuleAnalyzer.DiagnosticId).ToImmutableArray();

        diagnostics.Should().BeEmpty();
    }
}
