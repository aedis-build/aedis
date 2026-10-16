using FluentAssertions;
using NSign.Signatures;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>Mapeamento de nomes de componentes RFC 9421 e valores derivados da requisição.</summary>
public sealed class SignatureComponentsTests
{
    [Theory]
    [InlineData("@method", "@method")]
    [InlineData(" @Target-URI ", "@target-uri")]
    [InlineData("@authority", "@authority")]
    [InlineData("@scheme", "@scheme")]
    [InlineData("@request-target", "@request-target")]
    [InlineData("@path", "@path")]
    [InlineData("@query", "@query")]
    [InlineData("content-type", "content-type")]
    [InlineData("content-digest", "content-digest")]
    [InlineData("content-length", "content-length")]
    [InlineData("X-Custom", "x-custom")]
    public void Parse_mapeia_derivados_e_headers(string name, string expected) {
        SignatureComponents.Parse(name).ComponentName.Should().Be(expected);
    }

    [Fact]
    public void Parse_rejeita_derivado_desconhecido_e_nome_vazio() {
        var unknown = () => SignatureComponents.Parse("@status");
        var empty = () => SignatureComponents.Parse(" ");

        unknown.Should().Throw<NotSupportedException>().WithMessage("*@status*");
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DerivedValue_extrai_cada_componente_da_uri() {
        var uri = new Uri("HTTPS://Api.Example.com:8443/v1/orders?b=2&a=1");

        SignatureComponents.DerivedValue(SignatureComponent.Method, "post", uri).Should().Be("POST");
        SignatureComponents.DerivedValue(SignatureComponent.RequestTargetUri, "get", uri).Should().Be(uri.AbsoluteUri);
        SignatureComponents.DerivedValue(SignatureComponent.Authority, "get", uri).Should().Be("api.example.com:8443");
        SignatureComponents.DerivedValue(SignatureComponent.Scheme, "get", uri).Should().Be("https");
        SignatureComponents.DerivedValue(SignatureComponent.RequestTarget, "get", uri).Should().Be("/v1/orders?b=2&a=1");
        SignatureComponents.DerivedValue(SignatureComponent.Path, "get", uri).Should().Be("/v1/orders");
        SignatureComponents.DerivedValue(SignatureComponent.Query, "get", uri).Should().Be("?b=2&a=1");
        SignatureComponents.DerivedValue(SignatureComponent.Status, "get", uri).Should().BeNull();
    }

    [Fact]
    public void DerivedValue_porta_padrao_raiz_e_sem_query() {
        var uri = new Uri("https://api.example.com");

        SignatureComponents.DerivedValue(SignatureComponent.Authority, "get", uri).Should().Be("api.example.com");
        SignatureComponents.DerivedValue(SignatureComponent.Path, "get", uri).Should().Be("/");
        SignatureComponents.DerivedValue(SignatureComponent.Query, "get", uri).Should().Be("?");
    }

    [Fact]
    public void DerivedValue_exige_uri_absoluta() {
        var act = () => SignatureComponents.DerivedValue(SignatureComponent.Path, "get", new Uri("/x", UriKind.Relative));

        act.Should().Throw<ArgumentException>().WithParameterName("targetUri");
    }

    [Fact]
    public void QueryParamValues_devolve_todos_os_valores_decodificados_do_nome() {
        var uri = new Uri("https://h/p?q=a%20b&q=c+d&flag&other=1&q");

        SignatureComponents.QueryParamValues(uri, "q").Should().Equal("a b", "c d", "");
        SignatureComponents.QueryParamValues(uri, "flag").Should().Equal("");
        SignatureComponents.QueryParamValues(uri, "nada").Should().BeEmpty();
        SignatureComponents.QueryParamValues(new Uri("https://h/p"), "q").Should().BeEmpty();
    }
}
