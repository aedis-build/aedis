using System.Net;
using System.Text.Json;
using Aedis.AI.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aedis.AI.ManagedAgents.Tests;

/// <summary>
///     O motor contra a API do Managed Agents em memória: o que vai em cada requisição (teto de gasto, acesso das
///     memórias, overrides de skills, ordem do envio) e como o acompanhamento lê o stream — deduplica por id, para no
///     <see cref="SessionStopped" />, acusa stream encerrado antes da parada e, quando o stream cai, reconecta e
///     recupera pelo histórico o que se perdeu.
/// </summary>
public class ManagedAgentsEngineHttpTests
{
    private const string Mensagem = """{"type":"agent.message","id":"sevt_1","content":[{"type":"text","text":"oi"}]}""";
    private const string Ferramenta = """{"type":"agent.custom_tool_use","id":"sevt_2","name":"passar_para","input":{"cargo":"sdr"}}""";
    private const string Parada = """{"type":"session.status_idle","id":"sevt_3","stop_reason":{"type":"requires_action"}}""";

    private readonly FakeAnthropicHandler _api = new();

    private ManagedAgentsEngine Motor() => new(_api.Client(), TimeProvider.System, NullLogger<ManagedAgentsEngine>.Instance);

    private static NewSession Sessao(IReadOnlyList<string> skills) => new(
        "agent_1", "env_1",
        [new MountedMemory("mem_regras", true, "consulte as regras"), new MountedMemory("mem_cliente", false, "anote o cliente")],
        skills, 250, "Demanda 42", new Dictionary<string, string> { ["demanda"] = "42" });

    private JsonElement Corpo(string route) => JsonDocument.Parse(_api.Requests.Last(r => r.Route == route).Body).RootElement;

    private async Task<List<AgentEvent>> Acompanhar(string? sistema = null, string? mensagem = "oi") {
        var eventos = new List<AgentEvent>();
        await foreach (var evento in Motor().SendAndFollowAsync("sesn_1", sistema, mensagem, [], CancellationToken.None)) eventos.Add(evento);
        return eventos;
    }

    [Fact]
    public async Task Sessao_vai_com_teto_memorias_e_agente_sem_override() {
        _api.OnJson("POST /v1/sessions", """{"id":"sesn_1","type":"session"}""");

        var id = await Motor().CreateSessionAsync(Sessao([]), CancellationToken.None);

        id.Should().Be("sesn_1");
        var corpo = Corpo("POST /v1/sessions");
        corpo.GetProperty("agent").GetString().Should().Be("agent_1");
        corpo.GetProperty("budget").GetProperty("max_list_cost").GetProperty("amount").GetString().Should().Be("250");
        corpo.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("access").GetString()).Should().Equal("read_only", "read_write");
        corpo.GetProperty("metadata").GetProperty("demanda").GetString().Should().Be("42");
    }

    [Fact]
    public async Task Skills_da_sessao_substituem_as_do_agente() {
        _api.OnJson("POST /v1/sessions", """{"id":"sesn_1","type":"session"}""");

        await Motor().CreateSessionAsync(Sessao(["skill_a", "skill_b"]), CancellationToken.None);

        var agente = Corpo("POST /v1/sessions").GetProperty("agent");
        agente.GetProperty("type").GetString().Should().Be("agent_with_overrides");
        agente.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("skill_id").GetString()).Should().Equal("skill_a", "skill_b");
    }

    [Fact]
    public async Task Acompanhamento_deduplica_por_id_e_para_na_parada() {
        _api.OnStream("GET /v1/sessions/sesn_1/events/stream", Mensagem, Mensagem, """{"type":"span.model_request_end","id":"sevt_x"}""", Ferramenta, Parada, """{"type":"agent.message","id":"sevt_9","content":[]}""")
            .OnJson("POST /v1/sessions/sesn_1/events", "{}");

        var eventos = await Acompanhar(sistema: "contexto");

        eventos.Select(e => e.Id).Should().Equal("sevt_1", "sevt_2", "sevt_3");
        eventos[^1].Should().Be(new SessionStopped("sevt_3", StopReasons.RequiresAction));
        Corpo("POST /v1/sessions/sesn_1/events").GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).Should().Equal("user.message", "system.message");
    }

    [Fact]
    public async Task Stream_que_termina_antes_da_parada_vira_erro() {
        _api.OnStream("GET /v1/sessions/sesn_1/events/stream", Mensagem)
            .OnJson("POST /v1/sessions/sesn_1/events", "{}");

        var eventos = await Acompanhar();

        eventos.Should().HaveCount(2);
        eventos[^1].Should().BeOfType<SessionStopped>().Which.Reason.Should().Be(StopReasons.Error);
    }

    [Fact]
    public async Task Stream_que_cai_reconecta_e_recupera_pelo_historico_sem_repetir() {
        _api.On("GET /v1/sessions/sesn_1/events/stream", () => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .OnStream("GET /v1/sessions/sesn_1/events/stream")
            .OnJson("GET /v1/sessions/sesn_1/events", $$"""{"data":[{{Mensagem}},{{Mensagem}},{{Parada}}],"next_page":null}""")
            .OnJson("POST /v1/sessions/sesn_1/events", "{}");

        var eventos = await Acompanhar();

        eventos.Select(e => e.Id).Should().Equal("sevt_1", "sevt_3");
        _api.Requests.Count(r => r.Route == "GET /v1/sessions/sesn_1/events/stream").Should().Be(2);
        _api.Requests.Single(r => r.Route == "GET /v1/sessions/sesn_1/events").Query.Should().Contain("created_at");
    }

    [Fact]
    public async Task Historico_da_reconexao_percorre_todas_as_paginas() {
        _api.On("GET /v1/sessions/sesn_1/events/stream", () => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .OnStream("GET /v1/sessions/sesn_1/events/stream")
            .OnJson("GET /v1/sessions/sesn_1/events", $$"""{"data":[{{Mensagem}}],"next_page":"pagina_2"}""")
            .OnJson("GET /v1/sessions/sesn_1/events", $$"""{"data":[{{Parada}}],"next_page":null}""")
            .OnJson("POST /v1/sessions/sesn_1/events", "{}");

        var eventos = await Acompanhar();

        eventos.Select(e => e.Id).Should().Equal("sevt_1", "sevt_3");
        _api.Requests.Count(r => r.Route == "GET /v1/sessions/sesn_1/events").Should().Be(2);
    }

    [Fact]
    public async Task Stream_que_cai_alem_do_limite_propaga_a_falha() {
        _api.On("GET /v1/sessions/sesn_1/events/stream", () => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .OnJson("GET /v1/sessions/sesn_1/events", """{"data":[],"next_page":null}""")
            .OnJson("POST /v1/sessions/sesn_1/events", "{}");

        var acompanhar = () => Acompanhar();

        await acompanhar.Should().ThrowAsync<Exception>();
        _api.Requests.Count(r => r.Route == "GET /v1/sessions/sesn_1/events/stream").Should().Be(ManagedAgentsEngine.MaxReconnects + 1);
    }

    [Fact]
    public async Task Respostas_sem_acompanhar_vao_como_resultado_de_ferramenta() {
        _api.OnJson("POST /v1/sessions/sesn_1/events", "{}");

        await Motor().SendToolResultsAsync("sesn_1", [new ToolResult("sevt_2", "feito")], CancellationToken.None);

        var evento = Corpo("POST /v1/sessions/sesn_1/events").GetProperty("events")[0];
        evento.GetProperty("type").GetString().Should().Be("user.custom_tool_result");
        evento.GetProperty("custom_tool_use_id").GetString().Should().Be("sevt_2");
        evento.GetProperty("is_error").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"id":"sesn_1","usage":{"list_cost":{"amount":"137","currency":"USD"}}}""", 137)]
    [InlineData("""{"id":"sesn_1"}""", 0)]
    [InlineData("""{"id":"sesn_1","usage":{"list_cost":{"amount":"n/a"}}}""", 0)]
    public async Task Custo_vem_do_uso_da_sessao_ou_zero(string sessao, long centavos) {
        _api.OnJson("GET /v1/sessions/sesn_1", sessao);

        (await Motor().GetCostCentsAsync("sesn_1", CancellationToken.None)).Should().Be(centavos);
    }

    [Fact]
    public async Task Memoria_e_criada_e_escrita_pela_plataforma() {
        _api.OnJson("POST /v1/memory_stores", """{"id":"memstore_1","type":"memory_store"}""")
            .OnJson("POST /v1/memory_stores/memstore_1/memories", """{"id":"mem_1","type":"memory"}""");

        var id = await Motor().CreateMemoryStoreAsync("regras", "diretrizes da empresa", CancellationToken.None);
        await Motor().WriteMemoryAsync(id, "/regras.md", "# Regras", CancellationToken.None);

        id.Should().Be("memstore_1");
        Corpo("POST /v1/memory_stores").GetProperty("name").GetString().Should().Be("regras");
        var escrita = Corpo("POST /v1/memory_stores/memstore_1/memories");
        escrita.GetProperty("path").GetString().Should().Be("/regras.md");
        escrita.GetProperty("content").GetString().Should().Be("# Regras");
    }
}
