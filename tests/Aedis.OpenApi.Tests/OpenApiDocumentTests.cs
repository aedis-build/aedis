using System.Net;
using System.Reflection;
using System.Text.Json;
using Aedis.Core;
using Aedis.Diagnostics;
using Aedis.Hosting.AspNetCore;
using Aedis.Hosting.AspNetCore.Http;
using Aedis.Hypermedia.Abstractions;
using Aedis.OpenApi.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Xunit;

namespace Aedis.OpenApi.Tests;

/// <summary>
///     O plugin <c>Aedis.OpenApi</c> num host real: o documento 3.1 em <c>/openapi/v1.json</c> com as operações
///     <c>QUERY</c> sob <c>x-oai-additionalOperations</c>, <c>[Obsolete]</c> como <c>deprecated</c>, os erros
///     padrão de <c>[ProducesStandardErrors]</c>, o esquema Bearer, os ids de schema HAL, as opções da seção
///     <c>OpenApi</c> e a UI do Scalar em <c>/openapi</c>.
/// </summary>
public sealed class OpenApiDocumentTests {
    private static async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null,
        Action<OpenApiOptions>? configure = null, Action<ScalarOptions>? scalar = null) {
        var app = new OpenApiSampleApiHost(configure, scalar).BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings ?? []) {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement> DocumentAsync(WebApplication app, string url = "/openapi/v1.json") {
        var response = await app.GetTestClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Documento_expoe_operacoes_QUERY_sob_additionalOperations() {
        await using var app = await StartAsync();
        var json = await DocumentAsync(app);

        json.GetProperty("openapi").GetString().Should().StartWith("3.1");
        var path = json.GetProperty("paths").GetProperty("/v1/widgets");
        path.TryGetProperty("get", out _).Should().BeTrue("o GET continua sendo gerado normalmente");
        path.TryGetProperty("query", out _).Should().BeFalse("o QUERY não entra como verbo do Path Item em 3.1");
        var query = path.GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        query.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/WidgetSearch", "o schema já existe nos components (usado pelo POST)");
        query.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/WidgetResponseResourceCollection", "o schema já existe nos components (gerado pelo GET /all)");
        query.GetProperty("responses").TryGetProperty("422", out _).Should().BeTrue("[ProducesStandardErrors] vale também para o QUERY");
        query.GetProperty("tags")[0].GetString().Should().Be("Widgets");

        path.GetProperty("get").GetProperty("responses").TryGetProperty("409", out _).Should().BeTrue();
        path.GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString().Should().Be("#/components/schemas/WidgetResponseResource");
        json.GetProperty("paths").GetProperty("/v1/widgets/all").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString().Should().Be("#/components/schemas/WidgetResponseResourceCollection");

        var gadgets = json.GetProperty("paths").GetProperty("/v1/gadgets").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        gadgets.GetProperty("summary").GetString().Should().Be("Consulta gadgets");
        gadgets.GetProperty("description").GetString().Should().Be("Sem corpo e sem erros padrão");
        gadgets.GetProperty("deprecated").GetBoolean().Should().BeTrue();
        gadgets.TryGetProperty("requestBody", out _).Should().BeFalse("o corpo não tem schema nos components");
        var responses = gadgets.GetProperty("responses");
        responses.GetProperty("201").GetProperty("description").GetString().Should().Be("Created");
        responses.GetProperty("202").GetProperty("description").GetString().Should().Be("Accepted");
        responses.GetProperty("204").GetProperty("description").GetString().Should().Be("No Content");
        responses.GetProperty("400").GetProperty("description").GetString().Should().Be("Bad Request");
        responses.GetProperty("418").GetProperty("description").GetString().Should().Be("HTTP 418");
        responses.TryGetProperty("422", out _).Should().BeFalse("sem [ProducesStandardErrors]");

        var plain = json.GetProperty("paths").GetProperty("/v1/gadgets/plain").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        plain.GetProperty("responses").GetProperty("200").GetProperty("description").GetString().Should().Be("OK");
        json.GetProperty("components").GetProperty("schemas").TryGetProperty("PagedResultWidgetResponse", out _).Should().BeTrue("genéricos comuns viram {Base}{Args}");
    }

    [Fact]
    public async Task Obsolete_marca_deprecated_e_a_mensagem_vira_description() {
        await using var app = await StartAsync();
        var paths = (await DocumentAsync(app)).GetProperty("paths");

        var legado = paths.GetProperty("/legado").GetProperty("get");
        legado.GetProperty("deprecated").GetBoolean().Should().BeTrue();
        legado.GetProperty("description").GetString().Should().Be("Use /ping no lugar.");
        paths.GetProperty("/ping").GetProperty("get").TryGetProperty("deprecated", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Bearer_declarado_por_default_e_info_vem_das_opcoes() {
        await using var app = await StartAsync(new Dictionary<string, string?> {
            ["OpenApi:Title"] = "Widgets API",
            ["OpenApi:Version"] = "2024-01",
            ["OpenApi:Description"] = "Catálogo"
        });
        var json = await DocumentAsync(app);

        json.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString().Should().Be("bearer");
        json.GetProperty("security")[0].TryGetProperty("Bearer", out _).Should().BeTrue();
        var info = json.GetProperty("info");
        info.GetProperty("title").GetString().Should().Be("Widgets API");
        info.GetProperty("version").GetString().Should().Be("2024-01");
        info.GetProperty("description").GetString().Should().Be("Catálogo");
    }

    [Fact]
    public async Task Sem_Bearer_o_titulo_cai_no_assembly_de_entrada_e_o_prefixo_e_configuravel() {
        await using var app = await StartAsync(new Dictionary<string, string?> {
            ["OpenApi:IncludeBearerSecurity"] = "false",
            ["OpenApi:RoutePrefix"] = "docs/api",
            ["OpenApi:DocumentName"] = "current"
        });
        var json = await DocumentAsync(app, "/docs/api/current.json");

        json.GetProperty("info").GetProperty("title").GetString().Should().Be(Assembly.GetEntryAssembly()!.GetName().Name);
        json.TryGetProperty("security", out _).Should().BeFalse();
        (json.TryGetProperty("components", out var components) && components.TryGetProperty("securitySchemes", out _)).Should().BeFalse();
        (await app.GetTestClient().GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.GetTestClient().GetAsync("/docs/api/")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scalar_serve_a_UI_no_prefixo_e_aplica_o_configure() {
        var scalarConfigured = false;
        await using var app = await StartAsync(scalar: _ => scalarConfigured = true);

        (await app.GetTestClient().GetAsync("/openapi")).StatusCode.Should().Be(HttpStatusCode.Found, "o Scalar redireciona para a barra final");
        var response = await app.GetTestClient().GetAsync("/openapi/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("v1.json");
        scalarConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Configure_do_gerador_roda_depois_dos_padroes_do_Aedis() {
        await using var app = await StartAsync(configure: options => options.AddDocumentTransformer((document, _, _) => {
            document.Info.Contact = new Microsoft.OpenApi.OpenApiContact { Name = "Equipe" };
            return Task.CompletedTask;
        }));

        var info = (await DocumentAsync(app)).GetProperty("info");

        info.GetProperty("contact").GetProperty("name").GetString().Should().Be("Equipe");
        info.GetProperty("title").GetString().Should().NotBeNullOrEmpty("o Info dos padrões do Aedis foi preservado");
    }

    [Fact]
    public async Task Opcoes_invalidas_impedem_o_host_de_subir() {
        var act = () => StartAsync(new Dictionary<string, string?> { ["OpenApi:RoutePrefix"] = "/openapi/" });

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void Extensoes_rejeitam_argumentos_nulos() {
        var services = new ServiceCollection();

        var semServices = () => ((IServiceCollection)null!).AddAedisOpenApi(new ConfigurationBuilder().Build());
        var semConfiguracao = () => services.AddAedisOpenApi(null!);
        var semAppOpenApi = () => ((IEndpointRouteBuilder)null!).MapAedisOpenApi();
        var semAppScalar = () => ((IEndpointRouteBuilder)null!).MapAedisScalar();

        semServices.Should().Throw<ArgumentNullException>();
        semConfiguracao.Should().Throw<ArgumentNullException>();
        semAppOpenApi.Should().Throw<ArgumentNullException>();
        semAppScalar.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>Host com o plugin de OpenAPI + Scalar e controllers de exemplo (GET, POST e QUERY).</summary>
public sealed class OpenApiSampleApiHost(Action<OpenApiOptions>? configure, Action<ScalarOptions>? scalar) : AedisApiHost {
    /// <summary>Roda aberto em Development (sem provedor de identidade).</summary>
    protected override bool EnableAuthentication => false;

    /// <summary>Desliga a telemetria OTLP no teste.</summary>
    protected override bool EnableTelemetry => false;

    /// <summary>Policy cujo nome difere das roles, para provar que a extensão publica as roles resolvidas.</summary>
    protected override void ConfigureAuthorization(Microsoft.AspNetCore.Authorization.AuthorizationOptions options) {
        options.AddPolicy("orders:write", policy => policy.RequireRole("orders.writer", "orders.admin"));
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers().AddApplicationPart(typeof(OpenApiSampleApiHost).Assembly);
        services.AddAedisOpenApi(configuration, configure);
        services.AddScoped<FluentValidation.IValidator<CreateOrder>, CreateOrderValidator>();
        services.AddScoped<FluentValidation.IValidator<OrderFilter>, OrderFilterValidator>();
        services.AddScoped<FluentValidation.IValidator<Address>>(_ => new AddressValidator("BR"));
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapGet("/legado", [Obsolete("Use /ping no lugar.")] () => Results.Ok(new { message = "legado" }));
        app.MapGet("/ping", () => Results.Ok(new { message = "pong" }));
        app.MapAedisOpenApi();
        app.MapAedisScalar(scalar);
    }
}

/// <summary>Request de consulta do exemplo.</summary>
public sealed record WidgetSearch(string? Name, int? Page, int? PageSize);

/// <summary>Resposta do exemplo.</summary>
public sealed record WidgetResponse(int Id, string Name);

/// <summary>Controller com GET, POST e QUERY no mesmo path, com os erros padrão no controller inteiro.</summary>
[ApiController]
[Route("v1/widgets")]
[ProducesStandardErrors]
public sealed class WidgetsController : ControllerBase {
    /// <summary>Recurso único (schema <c>WidgetResponseResource</c>).</summary>
    [HttpGet]
    [ProducesResponseType<Resource<WidgetResponse>>(200)]
    public IActionResult Get() => Ok(new Resource<WidgetResponse>(new WidgetResponse(1, "w")));

    /// <summary>Coleção HAL (schema <c>WidgetResponseResourceCollection</c>).</summary>
    [HttpGet("all")]
    [ProducesResponseType<ResourceCollection<WidgetResponse>>(200)]
    public IActionResult GetAll() => Ok(new ResourceCollection<WidgetResponse>([]));

    /// <summary>Leva o <c>WidgetSearch</c> aos components, para o QUERY referenciá-lo.</summary>
    [HttpPost]
    [ProducesResponseType<PagedResult<WidgetResponse>>(200)]
    public IActionResult Post([FromBody] WidgetSearch request) => Ok(new PagedResult<WidgetResponse>([], 0, 1, 10));

    /// <summary>Lista por QUERY (coleção HAL).</summary>
    [HttpQuery]
    [ProducesResponseType<ResourceCollection<WidgetResponse>>(200)]
    public IActionResult Query([FromBody] WidgetSearch request) => Ok(new ResourceCollection<WidgetResponse>([]));
}

/// <summary>Controller QUERY obsoleto, com resumo/descrição, corpo sem schema nos components e vários status.</summary>
[ApiController]
[Route("v1/gadgets")]
[Obsolete("exemplo")]
public sealed class GadgetsController : ControllerBase {
    /// <summary>Consulta com corpo que só este QUERY usa.</summary>
    [HttpQuery]
    [EndpointSummary("Consulta gadgets")]
    [EndpointDescription("Sem corpo e sem erros padrão")]
    [ProducesResponseType<WidgetResponse>(201)]
    [ProducesResponseType(202)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(418)]
    public IActionResult Query([FromBody] GadgetFilter filter) => NoContent();

    /// <summary>Consulta sem nada declarado: ganha o 200 padrão.</summary>
    [HttpQuery("plain")]
    public IActionResult Plain() => NoContent();
}

/// <summary>Corpo usado somente por um QUERY (não entra nos components).</summary>
public sealed record GadgetFilter(string? Color);
