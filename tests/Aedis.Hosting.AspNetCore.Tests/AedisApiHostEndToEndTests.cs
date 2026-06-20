using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Exercita o <see cref="AedisApiHost" /> ponta-a-ponta através de <see cref="SampleApiHost" /> num
///     servidor de teste real: prova que a composição secure-by-default funciona de fato — cabeçalhos de
///     segurança em toda resposta, exceções traduzidas em ProblemDetails, validação 422, health,
///     Swagger opt-in e o portão de autenticação fail-closed.
/// </summary>
public sealed class AedisApiHostEndToEndTests
{
    private static async Task<WebApplication> StartAsync(AedisApiHost host, string environment = "Development") {
        var app = host.BuildApplication(["--environment", environment], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Endpoint_responde_e_carrega_os_cabecalhos_de_seguranca() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task BusinessException_vira_problem_details_com_o_status_efetivo() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/conflito");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ForbiddenException_vira_403() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/proibido");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Entrada_invalida_vira_422() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/pedidos", new SampleInput(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Entrada_valida_responde_200() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/pedidos", new SampleInput("aedis"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_de_health_responde() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_desligado_por_default_retorna_404() {
        await using var app = await StartAsync(new SampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_opt_in_expoe_o_documento_openapi() {
        await using var app = await StartAsync(new SwaggerSampleApiHost());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Autenticacao_desabilitada_fora_de_development_recusa_subir() {
        var host = new SampleApiHost();

        var act = () => host.BuildApplication(["--environment", "Production"]);

        act.Should().Throw<InvalidOperationException>("o host é fail-closed: não sobe inseguro em produção");
    }
}
