using System.Text.Json;
using Aedis.AI.Abstractions;
using Aedis.AI.ManagedAgents;
using Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aedis.AI.ManagedAgents.Tests;

/// <summary>
///     Tradução do protocolo do Managed Agents: eventos brutos da API viram <see cref="AgentEvent" /> (os sem uso são
///     descartados), o envio respeita a ordem exigida pela API (<c>system.message</c> por último) e a sessão não nasce
///     sem teto de gasto.
/// </summary>
public class ManagedAgentsEngineTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Mensagem_do_agente_junta_as_partes_de_texto() {
        var evento = ManagedAgentsEngine.Read(Json("""
            {"type":"agent.message","id":"sevt_1","content":[{"type":"text","text":"Olá, "},{"type":"image"},{"type":"text","text":"Lais."}]}
            """));

        evento.Should().Be(new AgentMessage("sevt_1", "Olá, Lais."));
    }

    [Fact]
    public void Ferramenta_propria_traz_nome_e_entrada() {
        var evento = ManagedAgentsEngine.Read(Json("""{"type":"agent.custom_tool_use","id":"sevt_2","name":"passar_para","input":{"cargo":"executivo-de-contas"}}"""));

        var chamada = evento.Should().BeOfType<ToolCall>().Subject;
        chamada.Name.Should().Be("passar_para");
        chamada.Input.GetProperty("cargo").GetString().Should().Be("executivo-de-contas");
    }

    [Theory]
    [InlineData("""{"type":"session.status_idle","id":"s","stop_reason":{"type":"requires_action","event_ids":["sevt_2"]}}""", StopReasons.RequiresAction)]
    [InlineData("""{"type":"session.status_idle","id":"s","stop_details":{"type":"budget_reached"}}""", StopReasons.BudgetReached)]
    [InlineData("""{"type":"session.status_idle","id":"s"}""", StopReasons.EndTurn)]
    [InlineData("""{"type":"session.status_terminated","id":"s"}""", StopReasons.Terminated)]
    public void Parada_le_stop_reason_ou_stop_details(string json, string motivo) {
        ManagedAgentsEngine.Read(Json(json)).Should().BeOfType<SessionStopped>().Which.Reason.Should().Be(motivo);
    }

    [Theory]
    [InlineData("""{"type":"span.model_request_end","id":"s"}""")]
    [InlineData("""{"id":"sem-tipo"}""")]
    public void Eventos_sem_uso_sao_ignorados(string json) {
        ManagedAgentsEngine.Read(Json(json)).Should().BeNull();
    }

    [Fact]
    public void Envio_poe_respostas_depois_a_mensagem_e_o_contexto_de_sistema_por_ultimo() {
        IReadOnlyList<ToolResult> respostas = [new ToolResult("sevt_1", "ok", IsError: true)];

        var tipos = JsonSerializer.SerializeToElement(ManagedAgentsEngine.Events("sistema", "oi", respostas))
            .EnumerateArray().Select(e => e.GetProperty("type").GetString());

        tipos.Should().Equal("user.custom_tool_result", "user.message", "system.message");
        JsonSerializer.SerializeToElement(ManagedAgentsEngine.Events(null, "oi", respostas))[0].GetProperty("is_error").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Sessao_sem_teto_de_gasto_positivo_e_recusada_antes_de_chamar_a_API(long teto) {
        var motor = new ManagedAgentsEngine(new AnthropicClient { ApiKey = "nao-usada" }, TimeProvider.System, NullLogger<ManagedAgentsEngine>.Instance);
        var sessao = new NewSession("agent_1", "env_1", [], [], teto, "t", new Dictionary<string, string>());

        var criar = () => motor.CreateSessionAsync(sessao, CancellationToken.None);

        await criar.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
