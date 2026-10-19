using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Aedis.Hosting.AspNetCore.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aedis.OpenApi.Tests;

/// <summary>
///     Os comentários XML do app entram no documento: <c>summary</c>/<c>remarks</c>/<c>param</c> das actions (GET,
///     POST e o QUERY sintetizado), descrições de DTOs e propriedades, precedência dos atributos sobre o XML e
///     no-op quando o assembly não tem arquivo de documentação.
/// </summary>
public sealed class XmlCommentsTests {
    private static async Task<JsonElement> DocumentAsync() {
        var app = new OpenApiSampleApiHost(null, null).BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await using var application = app;
        await app.StartAsync();

        return JsonDocument.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json")).RootElement;
    }

    [Fact]
    public async Task Summary_remarks_e_param_da_action_entram_no_GET_e_no_POST() {
        var paths = (await DocumentAsync()).GetProperty("paths");

        var get = paths.GetProperty("/v1/docs/{id}").GetProperty("get");
        get.GetProperty("summary").GetString().Should().Be("Busca um documento pelo id.");
        get.GetProperty("description").GetString().Should().Be("Devolve 404 quando o id não existe; null vira id.");
        var parameters = get.GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        parameters["id"].GetProperty("description").GetString().Should().Be("Identificador do documento.");
        parameters["lang"].GetProperty("description").GetString().Should().Be("Idioma, por exemplo pt.");

        var post = paths.GetProperty("/v1/docs").GetProperty("post");
        post.GetProperty("summary").GetString().Should().Be("Cria um documento a partir de NewDocument.");
        post.GetProperty("requestBody").GetProperty("description").GetString().Should().Be("Dados do documento novo.");
    }

    [Fact]
    public async Task Atributos_e_mensagem_do_Obsolete_prevalecem_sobre_o_XML() {
        var paths = (await DocumentAsync()).GetProperty("paths");

        var attr = paths.GetProperty("/v1/docs/attr").GetProperty("get");
        attr.GetProperty("summary").GetString().Should().Be("Resumo do atributo");
        attr.GetProperty("description").GetString().Should().Be("Descrição do atributo");

        var old = paths.GetProperty("/v1/docs/old").GetProperty("get");
        old.GetProperty("summary").GetString().Should().Be("Resumo do legado.");
        old.GetProperty("description").GetString().Should().Be("Use o GET por id.");
    }

    [Fact]
    public async Task QUERY_sintetizado_usa_o_XML_quando_nao_ha_atributo() {
        var paths = (await DocumentAsync()).GetProperty("paths");

        var query = paths.GetProperty("/v1/docs").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        query.GetProperty("summary").GetString().Should().Be("Consulta documentos pelo corpo.");
        query.GetProperty("description").GetString().Should().Be("Filtros no corpo, sem dado pessoal na URL.");

        var attr = paths.GetProperty("/v1/docs/attr").GetProperty("x-oai-additionalOperations").GetProperty("QUERY");
        attr.GetProperty("summary").GetString().Should().Be("Resumo QUERY do atributo");
    }

    [Fact]
    public async Task DTO_e_propriedades_ganham_descricao_inclusive_record_posicional() {
        var schemas = (await DocumentAsync()).GetProperty("components").GetProperty("schemas");

        var response = schemas.GetProperty("DocumentResponse");
        response.GetProperty("description").GetString().Should().Be("Documento devolvido pela API.");
        response.GetProperty("properties").GetProperty("id").GetProperty("description").GetString().Should().Be("Identificador.");
        response.GetProperty("properties").GetProperty("title").GetProperty("description").GetString().Should().Be("Título exibido.");

        var created = schemas.GetProperty("NewDocument");
        created.GetProperty("description").GetString().Should().Be("Dados de um documento novo.");
        created.GetProperty("properties").GetProperty("title").GetProperty("description").GetString().Should().Be("Título do documento novo.");
    }

    [Fact]
    public void Ids_seguem_as_regras_do_compilador_para_genericos_arrays_ref_e_aninhados() {
        var generic = typeof(Nested).GetMethod(nameof(Nested.Generic))!;
        var put = typeof(Box<int>).GetMethod(nameof(Box<int>.Put))!;
        var matrix = typeof(Nested).GetMethod(nameof(Nested.Matrix))!;

        XmlDocumentation.Summary(generic).Should().Be("Método genérico com coleção, array, ref e anulável.");
        XmlDocumentation.Summary(put).Should().Be("Guarda o item.", "o método do tipo fechado resolve para a definição aberta");
        XmlDocumentation.Summary(typeof(Nested)).Should().Be("Tipo aninhado.");
        XmlDocumentation.Summary(typeof(Nested).GetProperty(nameof(Nested.Name))!).Should().Be("Nome.");
        XmlDocumentation.IdOf(matrix).Should().Be("M:Aedis.OpenApi.Tests.XmlCommentsTests.Nested.Matrix(System.Int32[0:,0:])");
        XmlDocumentation.IdOf(typeof(Nested).GetField(nameof(Nested.Field))!).Should().BeEmpty();
    }

    [Fact]
    public void Assembly_sem_arquivo_XML_ou_com_arquivo_invalido_nao_documenta_nada() {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Aedis.NoDocs"), AssemblyBuilderAccess.Run).DefineDynamicModule("Aedis.NoDocs");
        var type = module.DefineType("Aedis.NoDocs.Undocumented", TypeAttributes.Public).CreateType();
        var malformed = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(malformed, "<doc><members><member name=\"T:X\">");

        try {
            XmlDocumentation.Summary(type).Should().BeNull();
            XmlDocumentation.Load(malformed).Should().BeEmpty();
            XmlDocumentation.Load(Path.Combine(Path.GetTempPath(), "nao-existe.xml")).Should().BeEmpty();
        }
        finally {
            File.Delete(malformed);
        }
    }

    /// <summary>Tipo aninhado.</summary>
    public sealed class Nested {
        /// <summary>Nome.</summary>
        public string? Name { get; set; }

        /// <summary>Campo sem id suportado.</summary>
        public int Field;

        /// <summary>Método genérico com coleção, array, ref e anulável.</summary>
        public static void Generic<T>(List<T> items, int[] ids, ref int count, Dictionary<string, int?> map) { }

        /// <summary>Matriz.</summary>
        public static void Matrix(int[,] cells) { }
    }

    /// <summary>Caixa genérica.</summary>
    public sealed class Box<T> {
        /// <summary>Guarda o item.</summary>
        public void Put(T item) { }
    }
}

/// <summary>Documento devolvido pela API.</summary>
public sealed class DocumentResponse {
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <summary>Título exibido.</summary>
    public string? Title { get; set; }
}

/// <summary>Dados de um documento novo.</summary>
/// <param name="Title">Título do documento novo.</param>
public sealed record NewDocument(string Title);

/// <summary>Filtro de documentos.</summary>
public sealed record DocumentFilter(string? Title);

/// <summary>Controller documentado por comentários XML.</summary>
[ApiController]
[Route("v1/docs")]
public sealed class DocumentsController : ControllerBase {
    /// <summary>Busca um documento pelo id.</summary>
    /// <remarks>Devolve 404 quando o id não existe; <see langword="null" /> vira <paramref name="id" />.</remarks>
    /// <param name="id">Identificador do documento.</param>
    /// <param name="lang">Idioma, por exemplo <c>pt</c>.</param>
    [HttpGet("{id:int}")]
    public ActionResult<DocumentResponse> Get(int id, [FromQuery] string? lang) => new DocumentResponse { Id = id };

    /// <summary>Cria um documento a partir de <see cref="NewDocument" />.</summary>
    /// <param name="request">Dados do documento novo.</param>
    [HttpPost]
    public ActionResult<DocumentResponse> Create([FromBody] NewDocument request) => new DocumentResponse();

    /// <summary>Resumo do XML ignorado.</summary>
    /// <remarks>Remarks do XML ignorados.</remarks>
    [HttpGet("attr")]
    [EndpointSummary("Resumo do atributo")]
    [EndpointDescription("Descrição do atributo")]
    public IActionResult Attr() => Ok();

    /// <summary>Resumo do legado.</summary>
    /// <remarks>Remarks perdem para a mensagem do Obsolete.</remarks>
    [HttpGet("old")]
    [Obsolete("Use o GET por id.")]
    public IActionResult Old() => Ok();

    /// <summary>Consulta documentos pelo corpo.</summary>
    /// <remarks>Filtros no corpo, sem dado pessoal na URL.</remarks>
    /// <param name="filter">Filtro.</param>
    [HttpQuery]
    public IActionResult Query([FromBody] DocumentFilter filter) => Ok();

    /// <summary>XML ignorado pelo QUERY com atributo.</summary>
    [HttpQuery("attr")]
    [EndpointSummary("Resumo QUERY do atributo")]
    public IActionResult QueryAttr() => Ok();
}
