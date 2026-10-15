using System.Text.Json;
using Aedis.Diagnostics;
using Aedis.Hosting.AspNetCore.Http;
using Aedis.Hosting.AspNetCore.OpenApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     O documento OpenAPI expõe as actions <c>QUERY</c> sob <c>x-oai-additionalOperations</c> do Path Item —
///     com corpo, respostas e os erros padrão de <c>[ProducesStandardErrors]</c> — sem derrubar a geração das
///     operações convencionais do mesmo path.
/// </summary>
public sealed class QueryOpenApiTests {
    [Fact]
    public async Task Documento_OpenAPI_expoe_operacoes_QUERY() {
        var app = new QuerySwaggerSampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await using var application = app;
        await app.StartAsync();

        var document = await app.GetTestClient().GetStringAsync("/swagger/v1/swagger.json");
        using var json = JsonDocument.Parse(document);
        var path = json.RootElement.GetProperty("paths").GetProperty("/v1/widgets");

        path.TryGetProperty("get", out _).Should().BeTrue("o GET continua sendo gerado normalmente");
        path.TryGetProperty("x-oai-additionalOperations", out var additional).Should().BeTrue(path.ToString());
        var query = additional.GetProperty("QUERY");
        query.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").ToString().Should().Contain("WidgetSearch");
        query.GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();
        query.GetProperty("responses").TryGetProperty("422", out _).Should().BeTrue("[ProducesStandardErrors] vale também para o QUERY");
        query.GetProperty("tags")[0].GetString().Should().Be("Widgets");

        path.GetProperty("get").GetProperty("responses").TryGetProperty("409", out _).Should().BeTrue();
    }
}

/// <summary>Host com Swagger e um controller QUERY, para o teste do documento.</summary>
public sealed class QuerySwaggerSampleApiHost : AedisApiHost {
    /// <inheritdoc cref="SampleApiHost.EnableAuthentication" />
    protected override bool EnableAuthentication => false;

    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <summary>Liga o Swagger para o teste.</summary>
    protected override bool EnableSwagger => true;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers().AddApplicationPart(typeof(QuerySwaggerSampleApiHost).Assembly);
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapControllers();
    }
}

/// <summary>Request de consulta do exemplo de OpenAPI.</summary>
public sealed record WidgetSearch(string? Name, int? Page, int? PageSize);

/// <summary>Resposta do exemplo de OpenAPI.</summary>
public sealed record WidgetResponse(int Id, string Name);

/// <summary>Controller com GET e QUERY no mesmo path.</summary>
[ApiController]
[Route("v1/widgets")]
[ProducesStandardErrors]
public sealed class WidgetsController : ControllerBase {
    /// <summary>Lista por GET.</summary>
    [HttpGet]
    [ProducesResponseType<WidgetResponse[]>(200)]
    public IActionResult Search([FromQuery] string? name) => Ok(Array.Empty<WidgetResponse>());

    /// <summary>Lista por QUERY.</summary>
    [HttpQuery]
    [ProducesResponseType<WidgetResponse[]>(200)]
    public IActionResult Query([FromBody] WidgetSearch request) => Ok(Array.Empty<WidgetResponse>());
}
