using Aedis.Http.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Http.Tests;

/// <summary>Fábricas de conteúdo agnóstico e sua conversão para HttpContent.</summary>
public sealed class AedisHttpContentTests
{
    [Fact]
    public async Task Json_serializa_em_camelCase_e_escapa_nao_ascii() {
        var content = AedisHttpContent.Json(new { OrderId = 1, Name = "ação" }).ToHttpContent();

        content.Headers.ContentType!.MediaType.Should().Be("application/json");
        content.Headers.ContentType.CharSet.Should().Be("utf-8");
        (await content.ReadAsStringAsync()).Should().Be("{\"orderId\":1,\"name\":\"a\\u00E7\\u00E3o\"}");
    }

    [Fact]
    public async Task Form_codifica_os_campos() {
        var content = AedisHttpContent.Form([new("grant_type", "client_credentials"), new("scope", "a b")]).ToHttpContent();

        content.Should().BeOfType<FormUrlEncodedContent>();
        (await content.ReadAsStringAsync()).Should().Be("grant_type=client_credentials&scope=a+b");
    }

    [Fact]
    public async Task Text_e_Bytes_preservam_o_content_type() {
        var text = AedisHttpContent.Text("<x/>", "application/xml").ToHttpContent();
        var bytes = AedisHttpContent.Bytes([1, 2, 3]).ToHttpContent();

        text.Headers.ContentType!.MediaType.Should().Be("application/xml");
        (await text.ReadAsStringAsync()).Should().Be("<x/>");
        bytes.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        (await bytes.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
    }

    private sealed class Unknown : AedisHttpContent;

    [Fact]
    public void Tipo_desconhecido_e_rejeitado() {
        var act = () => new Unknown().ToHttpContent();

        act.Should().Throw<NotSupportedException>().WithMessage("*Unknown*");
    }
}
