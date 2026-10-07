using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante que o <c>DeprecatedOperationFilter</c> registrado pelo host marca como <c>deprecated</c> as
///     operações anotadas com <see cref="ObsoleteAttribute" /> e usa a mensagem do atributo como descrição,
///     deixando as demais operações intactas.
/// </summary>
public sealed class SwaggerDeprecatedOperationTests
{
    private static async Task<JsonDocument> ObterDocumentoAsync() {
        var app = new SwaggerSampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        await app.DisposeAsync();
        return JsonDocument.Parse(json);
    }

    [Fact]
    public async Task Acao_obsoleta_marca_deprecated_no_documento() {
        using var documento = await ObterDocumentoAsync();

        var operacao = documento.RootElement.GetProperty("paths").GetProperty("/legado").GetProperty("get");

        operacao.GetProperty("deprecated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Mensagem_do_obsolete_vira_description() {
        using var documento = await ObterDocumentoAsync();

        var operacao = documento.RootElement.GetProperty("paths").GetProperty("/legado").GetProperty("get");

        operacao.GetProperty("description").GetString().Should().Be("Use /ping no lugar.");
    }

    [Fact]
    public async Task Acao_nao_obsoleta_nao_e_marcada() {
        using var documento = await ObterDocumentoAsync();

        var operacao = documento.RootElement.GetProperty("paths").GetProperty("/ping").GetProperty("get");

        operacao.TryGetProperty("deprecated", out var deprecated).Should().BeFalse(
            "uma operação sem [Obsolete] não deve ganhar a marca");
        deprecated.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
