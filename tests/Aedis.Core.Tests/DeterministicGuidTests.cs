using Aedis.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>
///     Garante que o GUID determinístico é estável para o mesmo par namespace/nome, muda com qualquer um dos
///     dois, é de versão 5 e que a forma composta não confunde fronteiras entre as partes.
/// </summary>
public class DeterministicGuidTests
{
    private static readonly Guid Namespace = DeterministicGuid.Namespaces.Url;

    [Fact]
    public void E_estavel_para_o_mesmo_namespace_e_nome() {
        var primeiro = DeterministicGuid.Create(Namespace, "contrato/123");
        var segundo = DeterministicGuid.Create(Namespace, "contrato/123");

        segundo.Should().Be(primeiro);
    }

    [Fact]
    public void Muda_quando_o_nome_muda() {
        DeterministicGuid.Create(Namespace, "a").Should().NotBe(DeterministicGuid.Create(Namespace, "b"));
    }

    [Fact]
    public void Muda_quando_o_namespace_muda() {
        DeterministicGuid.Create(DeterministicGuid.Namespaces.Dns, "x")
            .Should().NotBe(DeterministicGuid.Create(DeterministicGuid.Namespaces.Url, "x"));
    }

    [Fact]
    public void Gera_versao_5() {
        var guid = DeterministicGuid.Create(Namespace, "qualquer");

        guid.ToString("D")[14].Should().Be('5');
    }

    [Fact]
    public void Forma_composta_nao_confunde_fronteiras_entre_partes() {
        var ab_c = DeterministicGuid.Create(Namespace, "ab", "c");
        var a_bc = DeterministicGuid.Create(Namespace, "a", "bc");

        ab_c.Should().NotBe(a_bc);
    }

    [Fact]
    public void Forma_composta_respeita_a_ordem_das_partes() {
        DeterministicGuid.Create(Namespace, "a", "b").Should().NotBe(DeterministicGuid.Create(Namespace, "b", "a"));
    }

    [Fact]
    public void Nome_nulo_e_rejeitado() {
        var act = () => DeterministicGuid.Create(Namespace, (string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Composta_sem_partes_e_rejeitada() {
        var act = () => DeterministicGuid.Create(Namespace, Array.Empty<string>());

        act.Should().Throw<ArgumentException>();
    }
}
