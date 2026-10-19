using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aedis.Core;
using Aedis.Diagnostics;
using Aedis.Excel.Abstractions;
using Aedis.Hosting.Abstractions;
using Aedis.Hosting.AspNetCore;
using Aedis.Hosting.AspNetCore.Exports;
using Aedis.Hosting.AspNetCore.Http;
using Aedis.Hypermedia.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hypermedia.AspNetCore.Tests;

/// <summary>
///     Prova, num servidor de teste real com controllers, a camada Aedis Hypermedia ponta a ponta: links por
///     <em>action</em>, <c>_links</c> por item, os dois contratos de navegação (GET preserva a query string;
///     QUERY tem URL limpa e <c>_templates</c> com o request ecoado), os links de exportação em ambos, e a
///     exportação de fato — por URL no GET e por corpo no QUERY, em streaming no escopo <c>all</c>.
/// </summary>
public sealed class HypermediaEndToEndTests {
    private static readonly HttpMethod Query = new(HttpQueryAttribute.Method);

    private static async Task<WebApplication> StartAsync() {
        var app = new HypermediaSampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement> QueryAsync(HttpClient client, string url, object body) {
        var response = await client.SendAsync(new HttpRequestMessage(Query, url) { Content = JsonContent.Create(body) });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Recurso_carrega_links_resolvidos_por_action() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/samples/7");

        json.GetProperty("data").GetProperty("id").GetInt32().Should().Be(7);
        json.GetProperty("_links").GetProperty("self").GetProperty("href").GetString().Should().EndWith("/v1/samples/7");
        json.GetProperty("_links").GetProperty("collection").GetProperty("href").GetString().Should().EndWith("/v1/samples");
    }

    [Fact]
    public async Task Colecao_GET_preserva_filtros_na_query_string_e_nao_emite_templates() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/samples?name=abc&page=2&pageSize=2");

        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("items")[0].GetProperty("_links").GetProperty("self").GetProperty("href").GetString().Should().EndWith("/v1/samples/3");
        json.GetProperty("totalCount").GetInt32().Should().Be(5);

        var links = json.GetProperty("_links");
        links.GetProperty("self").GetProperty("method").GetString().Should().Be("GET");
        links.GetProperty("next").GetProperty("href").GetString().Should().Contain("name=abc").And.Contain("page=3").And.Contain("pageSize=2");
        links.GetProperty("prev").GetProperty("href").GetString().Should().Contain("page=1");
        links.GetProperty("last").GetProperty("href").GetString().Should().Contain("page=3");
        json.TryGetProperty("_templates", out _).Should().BeFalse();
    }

    [Fact]
    public async Task HttpQuery_roteia_e_faz_bind_de_FromBody() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var json = await QueryAsync(client, "/v1/samples", new { name = "abc", page = 1, pageSize = 3 });

        json.GetProperty("items").GetArrayLength().Should().Be(3);
        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Navegacao_QUERY_tem_href_limpo_e_template_com_body_ecoado() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var json = await QueryAsync(client, "/v1/samples", new { name = "abc", page = 2, pageSize = 2 });

        var next = json.GetProperty("_links").GetProperty("next");
        next.GetProperty("method").GetString().Should().Be("QUERY");
        next.GetProperty("href").GetString().Should().EndWith("/v1/samples").And.NotContain("?");

        var template = json.GetProperty("_templates").GetProperty("next");
        template.GetProperty("method").GetString().Should().Be("QUERY");
        template.GetProperty("target").GetString().Should().Be(next.GetProperty("href").GetString());
        var body = template.GetProperty("body");
        body.GetProperty("name").GetString().Should().Be("abc", "o request do chamador é ecoado");
        body.GetProperty("page").GetInt32().Should().Be(3);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.TryGetProperty("export", out _).Should().BeFalse("navegação não carrega descritor de export");
    }

    [Fact]
    public async Task Export_GET_poe_descritor_na_URL_e_QUERY_no_body() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var get = await client.GetFromJsonAsync<JsonElement>("/v1/samples?name=abc&page=1&pageSize=2");
        var getAll = get.GetProperty("_links").GetProperty("export-all-csv");
        getAll.GetProperty("method").GetString().Should().Be("GET");
        getAll.GetProperty("href").GetString().Should().Contain("name=abc").And.Contain("export.type=csv").And.Contain("export.mode=all");
        get.GetProperty("_links").GetProperty("export-page-xlsx").GetProperty("href").GetString().Should().Contain("page=1").And.Contain("export.mode=page");

        var query = await QueryAsync(client, "/v1/samples", new { name = "abc", page = 1, pageSize = 2 });
        var queryPage = query.GetProperty("_links").GetProperty("export-page-xlsx");
        queryPage.GetProperty("method").GetString().Should().Be("QUERY");
        queryPage.GetProperty("href").GetString().Should().NotContain("?");
        var body = query.GetProperty("_templates").GetProperty("export-page-xlsx").GetProperty("body");
        body.GetProperty("name").GetString().Should().Be("abc");
        body.GetProperty("export").GetProperty("type").GetString().Should().BeEquivalentTo("xlsx");
        body.GetProperty("export").GetProperty("mode").GetString().Should().BeEquivalentTo("page");
    }

    [Fact]
    public async Task Export_GET_executa_pelo_proprio_link_e_QUERY_all_streama_todas_as_paginas() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var listing = await client.GetFromJsonAsync<JsonElement>("/v1/samples?page=1&pageSize=2");
        var href = listing.GetProperty("_links").GetProperty("export-all-csv").GetProperty("href").GetString()!;
        var viaGet = await client.GetAsync(href);

        viaGet.StatusCode.Should().Be(HttpStatusCode.OK);
        viaGet.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        viaGet.Content.Headers.ContentDisposition!.FileName.Should().Be("samples.csv");
        (await viaGet.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(6, "cabeçalho + 5 linhas de 3 páginas");

        var viaQuery = await client.SendAsync(new HttpRequestMessage(Query, "/v1/samples") {
            Content = JsonContent.Create(new { pageSize = 2, export = new { type = "csv", mode = "all" } })
        });

        viaQuery.StatusCode.Should().Be(HttpStatusCode.OK);
        (await viaQuery.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(6);
    }

    [Fact]
    public async Task Export_de_formato_nao_permitido_retorna_412() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var response = await client.SendAsync(new HttpRequestMessage(Query, "/v1/samples/xlsx-only") {
            Content = JsonContent.Create(new { export = new { type = "csv", mode = "page" } })
        });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }
}

/// <summary>Host de exemplo com controllers, para o teste E2E de hipermídia.</summary>
public sealed class HypermediaSampleApiHost : AedisApiHost {
    /// <inheritdoc cref="SampleApiHost.EnableAuthentication" />
    protected override bool EnableAuthentication => false;

    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers().AddApplicationPart(typeof(HypermediaSampleApiHost).Assembly);
        services.AddAedisHypermedia().Resource<SampleResource, SampleResourceLinks>();
        services.AddAedisExcel();
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

}

/// <summary>Request de consulta do exemplo: filtro, paginação e export no mesmo DTO (GET e QUERY).</summary>
public sealed record SampleSearchRequest : PagedExportableRequest {
    /// <summary>Filtro por nome.</summary>
    public string? Name { get; init; }
}

/// <summary>Controller de exemplo que devolve recursos, coleções (GET e QUERY) e exportações.</summary>
[ApiController]
[Route("v1/samples")]
public sealed class SamplesController : ControllerBase {
    private static readonly SampleResource[] All = Enumerable.Range(1, 5).Select(i => new SampleResource(i, $"sample-{i}")).ToArray();

    private static readonly IReadOnlyList<ExcelColumn<SampleResource>> Columns = [
        new("Id", r => r.Id),
        new("Name", r => r.Name)
    ];

    private readonly IExcelWriter _excel;
    private readonly IHypermedia _hypermedia;

    /// <summary>Cria o controller com o motor de planilhas e o serviço de hipermídia.</summary>
    public SamplesController(IExcelWriter excel, IHypermedia hypermedia) {
        _excel = excel;
        _hypermedia = hypermedia;
    }

    /// <summary>Recupera um item pelo id, com seus links.</summary>
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) => Ok(_hypermedia.Resource(new SampleResource(id, $"sample-{id}")));

    /// <summary>Lista por GET (filtros na query string).</summary>
    [HttpGet]
    public Task<IActionResult> Search([FromQuery] SampleSearchRequest request, CancellationToken cancellationToken) =>
        ListAsync(request, ExportLinkOptions.CsvAndXlsx, [ExportFormat.Csv, ExportFormat.Xlsx], cancellationToken);

    /// <summary>Lista por QUERY (filtros no corpo).</summary>
    [HttpQuery]
    public Task<IActionResult> Query([FromBody] SampleSearchRequest request, CancellationToken cancellationToken) =>
        ListAsync(request, ExportLinkOptions.CsvAndXlsx, [ExportFormat.Csv, ExportFormat.Xlsx], cancellationToken);

    /// <summary>Variante que só aceita exportar em XLSX.</summary>
    [HttpQuery("xlsx-only")]
    public Task<IActionResult> QueryXlsxOnly([FromBody] SampleSearchRequest request, CancellationToken cancellationToken) =>
        ListAsync(request, ExportLinkOptions.XlsxOnly, null, cancellationToken);

    private async Task<IActionResult> ListAsync(SampleSearchRequest request, ExportLinkOptions links,
        IReadOnlyCollection<ExportFormat>? allowed, CancellationToken cancellationToken) {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 2;

        var export = await this.TryHandleCollectionExportAsync(_excel, request.Export, page, pageSize, FetchPageAsync, Columns,
            "samples", allowedFormats: allowed, cancellationToken: cancellationToken);
        if (export is not null) return export;

        var (items, total) = await FetchPageAsync(page, pageSize, cancellationToken);
        return Ok(_hypermedia.Collection(new PagedResult<SampleResource>(items, total, page, pageSize), links));
    }

    private static Task<(IReadOnlyList<SampleResource> Items, int Total)> FetchPageAsync(int page, int pageSize, CancellationToken _) {
        IReadOnlyList<SampleResource> items = All.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, All.Length));
    }
}

/// <summary>Modelo de resposta do exemplo de hipermídia.</summary>
public sealed record SampleResource(int Id, string Name);

/// <summary>Declara os links do <see cref="SampleResource" /> por action.</summary>
public sealed class SampleResourceLinks : IResourceLinks<SampleResource> {
    /// <inheritdoc />
    public void Configure(ILinkMap links, SampleResource model) {
        links.Self("GetById", new { id = model.Id });
        links.Collection("Search");
    }
}
