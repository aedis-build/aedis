using System.Text.Json;
using System.Text.Json.Serialization;
using Aedis.Hosting.AspNetCore.Http;
using Aedis.OpenApi.Abstractions;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aedis.OpenApi.Tests;

/// <summary>
///     As extensões de metadados das operações — <c>x-aedis-roles</c>/<c>x-aedis-anonymous</c> (autorização
///     resolvida das policies), <c>x-aedis-risk</c> (<c>[OperationRisk]</c>, action sobre controller) e
///     <c>x-aedis-rules</c> (regras FluentValidation do body/query com a mensagem do 422) — em GET, POST e na
///     operação QUERY sintetizada.
/// </summary>
public sealed class OperationMetadataTests {
    private static async Task<JsonElement> PathsAsync() {
        var app = new OpenApiSampleApiHost(null, null).BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await using var application = app;
        await app.StartAsync();

        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement.GetProperty("paths");
    }

    private static JsonElement Rule(JsonElement rules, string field, string type) =>
        rules.EnumerateArray().Single(r => r.TryGetProperty("field", out var f) && f.GetString() == field && r.GetProperty("type").GetString() == type);

    [Fact]
    public async Task Roles_vem_do_Authorize_e_das_policies_resolvidas_e_AllowAnonymous_marca_anonimo() {
        var paths = await PathsAsync();
        var orders = paths.GetProperty("/v1/orders");

        orders.GetProperty("post").GetProperty("x-aedis-roles").EnumerateArray().Select(r => r.GetString())
            .Should().Equal("admin", "ops");
        paths.GetProperty("/v1/orders/{id}").GetProperty("put").GetProperty("x-aedis-roles").EnumerateArray().Select(r => r.GetString())
            .Should().Equal(["orders.admin", "orders.writer", "support"], "as roles da policy, não o nome dela, mais as do atributo");
        orders.GetProperty("get").GetProperty("x-aedis-anonymous").GetBoolean().Should().BeTrue();
        orders.GetProperty("get").TryGetProperty("x-aedis-roles", out _).Should().BeFalse();

        var authenticated = paths.GetProperty("/v1/orders/{id}").GetProperty("get");
        authenticated.TryGetProperty("x-aedis-roles", out _).Should().BeFalse("só autenticação não gera extensão");
        authenticated.TryGetProperty("x-aedis-anonymous", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Risco_da_action_prevalece_sobre_o_da_controller_e_omite_o_motivo_vazio() {
        var paths = await PathsAsync();

        var post = paths.GetProperty("/v1/orders").GetProperty("post").GetProperty("x-aedis-risk");
        post.GetProperty("level").GetString().Should().Be("HIGH");
        post.GetProperty("reason").GetString().Should().Be("Moves money.");

        var inherited = paths.GetProperty("/v1/orders/{id}").GetProperty("get").GetProperty("x-aedis-risk");
        inherited.GetProperty("level").GetString().Should().Be("LOW");
        inherited.TryGetProperty("reason", out _).Should().BeFalse();
        paths.GetProperty("/v1/widgets").GetProperty("get").TryGetProperty("x-aedis-risk", out _).Should().BeFalse("sem [OperationRisk] não há extensão");
    }

    [Fact]
    public async Task Regras_do_body_saem_com_tipo_mensagem_parametros_condicional_e_filhos() {
        var rules = (await PathsAsync()).GetProperty("/v1/orders").GetProperty("post").GetProperty("x-aedis-rules");

        Rule(rules, "code", "NotEmpty").GetProperty("source").GetString().Should().Be("body");
        var length = Rule(rules, "code", "Length");
        length.GetProperty("parameters").GetProperty("min").GetInt32().Should().Be(3);
        length.GetProperty("parameters").GetProperty("max").GetInt32().Should().Be(50);
        length.GetProperty("message").GetString().Should().Be("Code must be between 3 and 50 characters");

        var end = Rule(rules, "endDate", "GreaterThanOrEqual");
        end.GetProperty("parameters").GetProperty("comparedTo").GetString().Should().Be("startDate");
        end.GetProperty("message").GetString().Should().Be("End date must be greater than or equal to start date");

        Rule(rules, "sku", "RegularExpression").GetProperty("parameters").GetProperty("pattern").GetString().Should().Be("^[A-Z]+$");

        var quantity = Rule(rules, "quantity", "Must");
        quantity.GetProperty("message").GetString().Should().NotContain("{").And.Contain("Quantity", "WithMessage(lambda) cai na mensagem padrão");
        quantity.TryGetProperty("conditional", out _).Should().BeFalse();

        Rule(rules, "note", "NotEmpty").GetProperty("conditional").GetBoolean().Should().BeTrue();
        Rule(rules, "ref", "NotEmpty").GetProperty("field").GetString().Should().Be("ref", "[JsonPropertyName] prevalece");
        Rule(rules, "tags[]", "ExactLength").GetProperty("message").GetString().Should().Be("Tags must have 3 characters");
        Rule(rules, "customer.name", "NotEmpty").GetProperty("message").GetString().Should().Be("Customer name is required");
        Rule(rules, "customer.address.country", "Equal").GetProperty("parameters").GetProperty("value").GetString().Should().Be("BR", "validator filho sem construtor vazio é resolvido do container");

        Rule(rules, "price", "GreaterThan").GetProperty("parameters").GetProperty("value").GetDecimal().Should().Be(0m);
        Rule(rules, "ratio", "InclusiveBetween").GetProperty("parameters").GetProperty("to").GetDouble().Should().Be(1.0);
        Rule(rules, "deadline", "LessThanOrEqual").GetProperty("parameters").GetProperty("value").GetString().Should().Be("2030-01-01");
        Rule(rules, "priority", "Equal").GetProperty("parameters").GetProperty("value").GetString().Should().Be("Normal");
        rules.EnumerateArray().Should().NotContain(r => r.GetProperty("message").GetString() == "Only in the import rule set");
    }

    [Fact]
    public async Task Objeto_de_query_e_QUERY_sintetizado_recebem_as_mesmas_extensoes() {
        var paths = await PathsAsync();

        var search = paths.GetProperty("/v1/orders/search").GetProperty("get").GetProperty("x-aedis-rules");
        var pageSize = Rule(search, "pageSize", "InclusiveBetween");
        pageSize.GetProperty("source").GetString().Should().Be("query");
        pageSize.GetProperty("parameters").GetProperty("from").GetInt32().Should().Be(1);
        pageSize.GetProperty("message").GetString().Should().StartWith("'Page Size'").And.Contain("200").And.NotContain("{", "placeholders que dependem do valor saem do texto");

        var query = paths.GetProperty("/v1/orders").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        query.GetProperty("x-aedis-roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("reader");
        query.GetProperty("x-aedis-risk").GetProperty("level").GetString().Should().Be("LOW");
        Rule(query.GetProperty("x-aedis-rules"), "pageSize", "InclusiveBetween").GetProperty("source").GetString().Should().Be("body");

        var anonymousQuery = paths.GetProperty("/v1/orders/public").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        anonymousQuery.GetProperty("x-aedis-anonymous").GetBoolean().Should().BeTrue();
        anonymousQuery.TryGetProperty("x-aedis-rules", out _).Should().BeFalse("sem validator não há regras");
    }
}

/// <summary>Prioridade do pedido do exemplo.</summary>
public enum OrderPriority { Normal, Urgent }

/// <summary>Endereço do cliente (validator filho resolvido do container).</summary>
public sealed record Address(string? Country);

/// <summary>Cliente do pedido (validator filho com construtor vazio).</summary>
public sealed record Customer(string? Name, Address? Address);

/// <summary>Corpo de criação do pedido, validado por <see cref="CreateOrderValidator" />.</summary>
public sealed class CreateOrder {
    public string? Code { get; set; }
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public bool Urgent { get; set; }
    [JsonPropertyName("ref")] public string? Reference { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly Deadline { get; set; }
    public decimal Price { get; set; }
    public double Ratio { get; set; }
    public OrderPriority Priority { get; set; }
    public List<string>? Tags { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>Filtro de busca, validado por <see cref="OrderFilterValidator" /> (GET por query e QUERY por body).</summary>
public sealed class OrderFilter {
    public string? Code { get; set; }
    public int PageSize { get; set; }
}

/// <summary>Regras do pedido: tamanho, comparação, regex, predicado, condicional, filhos, coleção e rule set nomeado.</summary>
public sealed class CreateOrderValidator : AbstractValidator<CreateOrder> {
    public CreateOrderValidator() {
        RuleFor(x => x.Code).NotEmpty().Length(3, 50).WithMessage("Code must be between {MinLength} and {MaxLength} characters");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).WithMessage("End date must be greater than or equal to start date");
        RuleFor(x => x.Sku).Matches("^[A-Z]+$");
        RuleFor(x => x.Quantity).Must(q => q > 0).WithMessage(x => $"Quantity {x.Quantity} must be positive");
        RuleFor(x => x.Note).NotEmpty().When(x => x.Urgent);
        RuleFor(x => x.Reference).NotEmpty();
        RuleForEach(x => x.Tags).Length(3).WithMessage("Tags must have 3 characters");
        RuleFor(x => x.Customer!).SetValidator(new CustomerValidator()).When(x => x.Customer is not null);
        RuleFor(x => x.Price).GreaterThan(0m);
        RuleFor(x => x.Ratio).InclusiveBetween(0.0, 1.0);
        RuleFor(x => x.Deadline).LessThanOrEqualTo(new DateOnly(2030, 1, 1));
        RuleFor(x => x.Priority).Equal(OrderPriority.Normal);
        RuleSet("import", () => RuleFor(x => x.Code).NotEmpty().WithMessage("Only in the import rule set"));
    }
}

/// <summary>Validator filho com construtor vazio.</summary>
public sealed class CustomerValidator : AbstractValidator<Customer> {
    public CustomerValidator() {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Customer name is required");
        RuleFor(x => x.Address!).SetValidator(new AddressValidator("BR"));
    }
}

/// <summary>Validator filho sem construtor vazio: a extensão o resolve do container.</summary>
public sealed class AddressValidator : AbstractValidator<Address> {
    public AddressValidator(string country) {
        RuleFor(x => x.Country).Equal(country);
    }
}

/// <summary>Regras do filtro de busca.</summary>
public sealed class OrderFilterValidator : AbstractValidator<OrderFilter> {
    public OrderFilterValidator() {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>Controller com autorização por roles/policy, risco na controller e nas actions, e validators.</summary>
[ApiController]
[Route("v1/orders")]
[Authorize]
[OperationRisk(RiskLevel.Low)]
public sealed class OrdersController : ControllerBase {
    /// <summary>Anônimo.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult List() => Ok();

    /// <summary>Só autenticação; herda o risco da controller.</summary>
    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id) => Ok();

    /// <summary>Roles do atributo, risco da action, regras do body.</summary>
    [HttpPost]
    [Authorize(Roles = "ops, admin")]
    [OperationRisk(RiskLevel.High, "Moves money.")]
    public IActionResult Create([FromBody] CreateOrder order) => Ok();

    /// <summary>Roles da policy resolvida mais as do atributo.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "orders:write", Roles = "support")]
    public IActionResult Update(Guid id) => Ok();

    /// <summary>Objeto de query validado.</summary>
    [HttpGet("search")]
    public IActionResult Search([FromQuery] OrderFilter filter) => Ok();

    /// <summary>QUERY com roles, risco herdado e regras do body.</summary>
    [HttpQuery]
    [Authorize(Roles = "reader")]
    public IActionResult Query([FromBody] OrderFilter filter) => Ok();

    /// <summary>QUERY anônimo sem validator.</summary>
    [HttpQuery("public")]
    [AllowAnonymous]
    public IActionResult PublicQuery() => Ok();
}
