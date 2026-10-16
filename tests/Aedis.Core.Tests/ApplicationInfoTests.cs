using Aedis.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Aedis.Core.Tests;

/// <summary>Testes de <see cref="ApplicationInfo" />: nome, nome de exibição e sobrescrita.</summary>
public class ApplicationInfoTests : IDisposable
{
    /// <summary>Volta ao nome derivado do executável ao fim de cada teste.</summary>
    public void Dispose() {
        ApplicationInfo.ResetName();
        GC.SuppressFinalize(this);
    }

    /// <summary>Sem sobrescrita, o nome vem do executável em minúsculas.</summary>
    [Fact]
    public void Name_DerivaDoExecutavel_EmMinusculas() {
        ApplicationInfo.ResetName();

        ApplicationInfo.Name.Should().NotBeNullOrWhiteSpace().And.Be(ApplicationInfo.Name.ToLowerInvariant());
    }

    /// <summary>SetName normaliza para minúsculas e DisplayName capitaliza cada palavra.</summary>
    [Fact]
    public void SetName_SobrescreveNome_EDisplayNameCapitaliza() {
        ApplicationInfo.SetName("Order-Service.api_v2");

        ApplicationInfo.Name.Should().Be("order-service.api_v2");
        ApplicationInfo.DisplayName.Should().Be("Order Service Api V2");
    }

    /// <summary>Nome vazio é rejeitado.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetName_Vazio_Lanca(string name) {
        var act = () => ApplicationInfo.SetName(name);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    /// <summary>ResetName volta ao nome derivado do executável.</summary>
    [Fact]
    public void ResetName_VoltaAoNomeDoExecutavel() {
        ApplicationInfo.SetName("custom");
        ApplicationInfo.ResetName();

        ApplicationInfo.Name.Should().NotBe("custom");
    }

    /// <summary>UtcNow é o relógio UTC do sistema.</summary>
    [Fact]
    public void UtcNow_EUtc() {
        ApplicationInfo.UtcNow.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        ApplicationInfo.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }
}
