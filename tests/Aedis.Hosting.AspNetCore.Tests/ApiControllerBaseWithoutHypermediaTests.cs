using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aedis.Commands.Abstractions;
using Aedis.Core;
using Aedis.Diagnostics;
using Aedis.Hosting.AspNetCore.Controllers;
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
///     Sem o plugin <c>Aedis.Hypermedia.AspNetCore</c>, os helpers do <see cref="ApiControllerBase" /> degradam
///     para o modelo cru: <c>OkResource</c> devolve o objeto (404 quando nulo), <c>OkCollection</c> devolve a
///     <c>PagedResult</c> e <c>CreatedResource</c> devolve 201 com <c>Location</c> e o modelo — nenhum
///     <c>_links</c> na resposta.
/// </summary>
public sealed class ApiControllerBaseWithoutHypermediaTests
{
    private static async Task<WebApplication> StartAsync() {
        var app = new PlainControllersApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task OkResource_sem_plugin_devolve_o_modelo_cru_e_404_quando_nulo() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var one = await client.GetFromJsonAsync<JsonElement>("/v1/items/3");
        one.GetProperty("id").GetInt32().Should().Be(3);
        one.TryGetProperty("_links", out _).Should().BeFalse();

        (await client.GetAsync("/v1/items/0")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OkCollection_e_CreatedResource_sem_plugin_devolvem_a_pagina_e_o_modelo_crus() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var list = await client.GetFromJsonAsync<JsonElement>("/v1/items");
        list.GetProperty("items").GetArrayLength().Should().Be(2);
        list.GetProperty("total").GetInt32().Should().Be(2);
        list.TryGetProperty("_links", out _).Should().BeFalse();

        var created = await client.PostAsJsonAsync("/v1/items", new { name = "novo" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().EndWith("/v1/items/9");
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("novo");
        body.TryGetProperty("_links", out _).Should().BeFalse();
    }
}

/// <summary>Host de exemplo só com controllers, sem hipermídia nem OpenAPI.</summary>
public sealed class PlainControllersApiHost : AedisApiHost
{
    /// <inheritdoc cref="SampleApiHost.EnableAuthentication" />
    protected override bool EnableAuthentication => false;

    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers().AddApplicationPart(typeof(PlainControllersApiHost).Assembly);
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }
}

/// <summary>Modelo de resposta do exemplo sem hipermídia.</summary>
public sealed record ItemResource(int Id, string Name);

/// <summary>Corpo de criação do exemplo sem hipermídia.</summary>
public sealed record NewItem(string Name);

/// <summary>Controller derivado de <see cref="ApiControllerBase" /> servido sem o plugin de hipermídia.</summary>
[Route("v1/items")]
public sealed class ItemsController(ICommandExecutor commandExecutor) : ApiControllerBase(commandExecutor)
{
    /// <summary>Devolve o item, ou 404 quando o id é zero.</summary>
    [HttpGet("{id:int}")]
    public IActionResult GetItem(int id) => OkResource(id == 0 ? null : new ItemResource(id, $"item-{id}"));

    /// <summary>Lista dois itens numa página.</summary>
    [HttpGet]
    public IActionResult ListItems() => OkCollection(new PagedResult<ItemResource>([new(1, "a"), new(2, "b")], 2, 1, 10));

    /// <summary>Cria o item 9.</summary>
    [HttpPost]
    public IActionResult CreateItem([FromBody] NewItem item) => CreatedResource(new ItemResource(9, item.Name), nameof(GetItem), new { id = 9 });
}
