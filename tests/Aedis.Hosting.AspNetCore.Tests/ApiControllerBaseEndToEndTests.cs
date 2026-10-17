using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aedis.Commands.Abstractions;
using Aedis.Core;
using Aedis.Hosting.AspNetCore.Controllers;
using Aedis.Hosting.AspNetCore.Hypermedia;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Controller derivado de <see cref="ApiControllerBase" /> servido pelo host de exemplo: recurso, coleção,
///     criação com links por action (todos os verbos do ILinkMap) e 422 de validação de modelo.
/// </summary>
public sealed class ApiControllerBaseEndToEndTests
{
    private static async Task<WebApplication> StartAsync() {
        var app = new HypermediaSampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
            builder.Services.AddAedisHypermedia().Resource<OrderResource, OrderResourceLinks>();
            builder.Services.AddScoped<IValidator<NewOrder>, NewOrderValidator>();
        });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Recurso_colecao_e_criacao_passam_pelo_ApiControllerBase() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var one = await client.GetFromJsonAsync<JsonElement>("/v1/orders/3");
        var links = one.GetProperty("_links");
        links.GetProperty("self").GetProperty("href").GetString().Should().EndWith("/v1/orders/3");
        links.GetProperty("cancel").GetProperty("method").GetString().Should().Be("POST");
        links.GetProperty("replace").GetProperty("method").GetString().Should().Be("PUT");
        links.GetProperty("remove").GetProperty("method").GetString().Should().Be("DELETE");
        links.GetProperty("audit").GetProperty("method").GetString().Should().Be("GET");
        links.GetProperty("ship").GetProperty("method").GetString().Should().Be("PATCH");

        var list = await client.GetFromJsonAsync<JsonElement>("/v1/orders");
        list.GetProperty("items").GetArrayLength().Should().Be(2);
        list.GetProperty("totalCount").GetInt32().Should().Be(2);

        var created = await client.PostAsJsonAsync("/v1/orders", new { name = "novo" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().EndWith("/v1/orders/9");
        (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("_links").GetProperty("self").GetProperty("href")
            .GetString().Should().EndWith("/v1/orders/9");
    }

    [Fact]
    public async Task Modelo_invalido_vira_422_problem_details_com_erros() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/v1/orders", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Unprocessable Entity");
        problem.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
    }
}

public sealed record OrderResource(int Id, string Name);

public sealed record NewOrder(string Name);

public sealed class NewOrderValidator : AbstractValidator<NewOrder>
{
    public NewOrderValidator() => RuleFor(o => o.Name).NotEmpty();
}

public sealed class OrderResourceLinks : ResourceLinks<OrderResource>
{
    protected override void Configure(ILinkMap links, OrderResource model) {
        links.Self("GetOrder", new { id = model.Id });
        links.Collection("ListOrders");
        links.Get("audit", "GetOrder", new { id = model.Id });
        links.Post("cancel", "CreateOrder");
        links.Put("replace", "GetOrder", new { id = model.Id });
        links.Delete("remove", "GetOrder", new { id = model.Id });
        links.Action("ship", "PATCH", "GetOrder", new { id = model.Id });
    }
}

[Route("v1/orders")]
public sealed class OrdersController(ICommandExecutor commandExecutor) : ApiControllerBase(commandExecutor)
{
    [HttpGet("{id:int}", Name = "GetOrder")]
    public IActionResult GetOrder(int id) => OkResource(new OrderResource(id, $"order-{id}"));

    [HttpGet(Name = "ListOrders")]
    public IActionResult ListOrders() =>
        OkCollection(new PagedResult<OrderResource>([new(1, "a"), new(2, "b")], 2, 1, 10));

    [HttpPost(Name = "CreateOrder")]
    public IActionResult CreateOrder([FromBody] NewOrder order) =>
        CreatedResource(new OrderResource(9, order.Name), nameof(GetOrder), new { id = 9 });
}
