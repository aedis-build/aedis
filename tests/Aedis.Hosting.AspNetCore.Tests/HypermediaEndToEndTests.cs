using System.Net.Http.Json;
using System.Text.Json;
using Aedis.Core;
using Aedis.Diagnostics;
using Aedis.Hosting.AspNetCore.Hypermedia;
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
///     Prova, num servidor de teste real com controllers, que a camada Aedis Hypermedia resolve os links por
///     <em>action</em> (via <c>LinkGenerator</c>) e carimba <c>_links</c> por item nas coleções paginadas —
///     ou seja, que o uso ergonômico do <see cref="ResourceLinks{T}" /> funciona ponta a ponta.
/// </summary>
public sealed class HypermediaEndToEndTests {
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
    public async Task Colecao_carrega_links_por_item_e_paginacao() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/samples?page=1&pageSize=2");

        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("items")[0].GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().EndWith("/v1/samples/1");
        json.GetProperty("totalCount").GetInt32().Should().Be(35);
        json.GetProperty("_links").TryGetProperty("next", out _).Should().BeTrue();
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
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapControllers();
    }
}

/// <summary>Controller de exemplo que devolve recursos e coleções de hipermídia.</summary>
[ApiController]
[Route("v1/samples")]
public sealed class SamplesController : ControllerBase {
    /// <summary>Recupera um item pelo id, com seus links.</summary>
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) => this.AsResource(new SampleResource(id, $"sample-{id}"));

    /// <summary>Lista itens paginados, com links por item e de paginação.</summary>
    [HttpGet]
    public IActionResult Search([FromQuery] int page = 1, [FromQuery] int pageSize = 2) {
        var items = new[] { new SampleResource(1, "a"), new SampleResource(2, "b") };
        return this.AsCollection(new PagedResult<SampleResource>(items, 35, page, pageSize));
    }
}

/// <summary>Modelo de resposta do exemplo de hipermídia.</summary>
public sealed record SampleResource(int Id, string Name);

/// <summary>Declara os links do <see cref="SampleResource" /> por action.</summary>
public sealed class SampleResourceLinks : ResourceLinks<SampleResource> {
    /// <inheritdoc />
    protected override void Configure(ILinkMap links, SampleResource model) {
        links.Self("GetById", new { id = model.Id });
        links.Collection("Search");
    }
}
