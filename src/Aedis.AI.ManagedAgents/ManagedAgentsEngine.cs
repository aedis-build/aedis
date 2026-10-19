using System.Runtime.CompilerServices;
using System.Text.Json;
using Aedis.AI.Abstractions;
using Anthropic;
using Anthropic.Models.Beta.MemoryStores;
using Anthropic.Models.Beta.MemoryStores.Memories;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using Microsoft.Extensions.Logging;

namespace Aedis.AI.ManagedAgents;

/// <summary>
///     <see cref="IAgentEngine" /> sobre o Claude Managed Agents (beta), com o SDK oficial da Anthropic. Os corpos são
///     montados no formato da API (<c>FromRawUnchecked</c>) e os eventos são lidos do JSON bruto, o que isola o
///     contrato das uniões tipadas do SDK, que mudam entre versões.
/// </summary>
/// <remarks>
///     O acompanhamento abre o stream antes do envio (stream-first): o que acontecer antes de ele conectar chega em
///     lote, sem perda. Se o stream cair, reconecta até <see cref="MaxReconnects" /> vezes e cobre o intervalo sem
///     stream com o histórico desde o envio, deduplicado por id.
/// </remarks>
public sealed class ManagedAgentsEngine(AnthropicClient anthropic, TimeProvider clock, ILogger<ManagedAgentsEngine> log) : IAgentEngine
{
    /// <summary>Reconexões do stream de eventos por acompanhamento antes de propagar a falha.</summary>
    public const int MaxReconnects = 3;

    private static readonly IReadOnlyDictionary<string, JsonElement> Empty = new Dictionary<string, JsonElement>();

    /// <inheritdoc />
    /// <remarks>
    ///     Com <see cref="NewSession.Skills" /> preenchido, a sessão usa <c>agent_with_overrides</c>, que troca a lista
    ///     inteira de skills do agente. O teto de gasto é obrigatório: <see cref="NewSession.BudgetCents" /> menor ou
    ///     igual a zero lança <see cref="ArgumentOutOfRangeException" /> antes de qualquer chamada.
    /// </remarks>
    public async Task<string> CreateSessionAsync(NewSession session, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(session.BudgetCents);

        object agent = session.Skills.Count == 0
            ? session.AgentId
            : new {
                type = "agent_with_overrides",
                id = session.AgentId,
                skills = session.Skills.Select(s => new { type = "custom", skill_id = s, version = "latest" })
            };
        var body = Body(new {
            agent,
            environment_id = session.EnvironmentId,
            title = session.Title,
            metadata = session.Metadata,
            budget = new { type = "limit", max_list_cost = new { amount = session.BudgetCents.ToString(), currency = "USD" } },
            resources = session.Memories.Select(m => new {
                type = "memory_store",
                memory_store_id = m.MemoryStoreId,
                access = m.ReadOnly ? "read_only" : "read_write",
                instructions = m.Instructions
            })
        });
        var created = await anthropic.Beta.Sessions.Create(SessionCreateParams.FromRawUnchecked(Empty, Empty, body), ct);
        return created.ID;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     O stream tem um cancelamento próprio: um iterador assíncrono não pode ser liberado com leitura pendente,
    ///     então a leitura é cancelada e aguardada antes do <c>DisposeAsync</c>. Se o stream terminar antes de a
    ///     sessão parar, emite <see cref="SessionStopped" /> com <see cref="StopReasons.Error" />.
    /// </remarks>
    public async IAsyncEnumerable<AgentEvent> SendAndFollowAsync(string sessionId, string? system, string? message, IReadOnlyList<ToolResult> toolResults, [EnumeratorCancellation] CancellationToken ct) {
        var seen = new HashSet<string>();
        var sentAt = clock.GetUtcNow().AddSeconds(-5);
        using var streamEnd = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stream = anthropic.Beta.Sessions.Events.StreamStreaming(sessionId, new EventStreamParams(), streamEnd.Token).GetAsyncEnumerator(streamEnd.Token);
        var next = Task.FromResult(false);
        try {
            next = stream.MoveNextAsync().AsTask();
            await SendAsync(sessionId, Events(system, message, toolResults), ct);

            var reconnects = 0;
            while (true) {
                var hasNext = false;
                Exception? drop = null;
                try {
                    hasNext = await next;
                } catch (Exception ex) when (ex is not OperationCanceledException && reconnects < MaxReconnects) {
                    drop = ex;
                }

                if (drop is not null) {
                    reconnects++;
                    log.LogWarning(drop, "Stream da sessão {SessionId} caiu; reconectando ({Attempt}/{Max})", sessionId, reconnects, MaxReconnects);
                    await stream.DisposeAsync();
                    stream = anthropic.Beta.Sessions.Events.StreamStreaming(sessionId, new EventStreamParams(), streamEnd.Token).GetAsyncEnumerator(streamEnd.Token);
                    next = stream.MoveNextAsync().AsTask();
                    await foreach (var raw in HistorySinceAsync(sessionId, sentAt, ct)) {
                        if (Read(raw) is { } replayed && seen.Add(replayed.Id)) {
                            yield return replayed;
                            if (replayed is SessionStopped) {
                                yield break;
                            }
                        }
                    }

                    continue;
                }

                if (!hasNext) {
                    yield return new SessionStopped(string.Empty, StopReasons.Error, "O stream terminou antes de a sessão parar.");
                    yield break;
                }

                if (Read(stream.Current.Json) is { } read && seen.Add(read.Id)) {
                    yield return read;
                    if (read is SessionStopped) {
                        yield break;
                    }
                }

                next = stream.MoveNextAsync().AsTask();
            }
        } finally {
            await streamEnd.CancelAsync();
            await DrainAsync(next);
            await stream.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public Task SendToolResultsAsync(string sessionId, IReadOnlyList<ToolResult> toolResults, CancellationToken ct) =>
        SendAsync(sessionId, Events(null, null, toolResults), ct);

    /// <inheritdoc />
    public async Task<long> GetCostCentsAsync(string sessionId, CancellationToken ct) {
        var session = await anthropic.Beta.Sessions.Retrieve(sessionId, new SessionRetrieveParams(), ct);
        return session.RawData.TryGetValue("usage", out var usage)
               && usage.ValueKind == JsonValueKind.Object
               && usage.TryGetProperty("list_cost", out var cost)
               && cost.TryGetProperty("amount", out var amount)
               && long.TryParse(amount.GetString(), out var cents)
            ? cents
            : 0;
    }

    /// <inheritdoc />
    public async Task<string> CreateMemoryStoreAsync(string name, string description, CancellationToken ct) {
        var created = await anthropic.Beta.MemoryStores.Create(
            MemoryStoreCreateParams.FromRawUnchecked(Empty, Empty, Body(new { name, description })), ct);
        return created.ID;
    }

    /// <inheritdoc />
    public async Task WriteMemoryAsync(string memoryStoreId, string path, string content, CancellationToken ct) =>
        await anthropic.Beta.MemoryStores.Memories.Create(
            MemoryCreateParams.FromRawUnchecked(Empty, Empty, Body(new { path, content }), memoryStoreId), ct);

    private async Task SendAsync(string sessionId, IEnumerable<object> events, CancellationToken ct) =>
        await anthropic.Beta.Sessions.Events.Send(EventSendParams.FromRawUnchecked(Empty, Empty, Body(new { events }), sessionId), ct);

    private async IAsyncEnumerable<JsonElement> HistorySinceAsync(string sessionId, DateTimeOffset since, [EnumeratorCancellation] CancellationToken ct) {
        var page = await anthropic.Beta.Sessions.Events.List(sessionId, new EventListParams { CreatedAtGte = since }, ct);
        while (true) {
            foreach (var item in page.Items) {
                yield return item.Json;
            }

            if (!page.HasNext()) {
                yield break;
            }

            page = await page.Next();
        }
    }

    /// <summary>
    ///     Aguarda a leitura pendente interrompida de propósito na limpeza, engolindo a falha: o erro que importa, se
    ///     houver, é o que já está subindo.
    /// </summary>
    private static async Task DrainAsync(Task pending) {
        try {
            await pending;
        } catch (Exception) {
        }
    }

    /// <summary>
    ///     Eventos a enviar, no formato da API: respostas de ferramentas, depois a mensagem do usuário e, por último, o
    ///     contexto de sistema — a API exige o <c>system.message</c> como último evento do envio.
    /// </summary>
    internal static IEnumerable<object> Events(string? system, string? message, IReadOnlyList<ToolResult> toolResults) {
        var events = new List<object>();
        foreach (var result in toolResults) {
            events.Add(new {
                type = "user.custom_tool_result",
                custom_tool_use_id = result.ToolUseId,
                content = new[] { new { type = "text", text = result.Text } },
                is_error = result.IsError
            });
        }

        if (message is not null) {
            events.Add(new { type = "user.message", content = new[] { new { type = "text", text = message } } });
        }

        if (system is not null) {
            events.Add(new { type = "system.message", content = new[] { new { type = "text", text = system } } });
        }

        return events;
    }

    /// <summary>Traduz um evento bruto da API para <see cref="AgentEvent" />; devolve <c>null</c> para os que não interessam.</summary>
    internal static AgentEvent? Read(JsonElement raw) {
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("type", out var type)) {
            return null;
        }

        var id = raw.TryGetProperty("id", out var idJson) ? idJson.GetString() ?? string.Empty : string.Empty;
        switch (type.GetString()) {
            case "agent.message":
                var parts = raw.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                    ? content.EnumerateArray()
                        .Where(p => p.TryGetProperty("type", out var t) && t.GetString() == "text" && p.TryGetProperty("text", out _))
                        .Select(p => p.GetProperty("text").GetString())
                    : [];
                return new AgentMessage(id, string.Concat(parts));
            case "agent.custom_tool_use":
                return new ToolCall(
                    id,
                    raw.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    raw.TryGetProperty("input", out var input) ? input.Clone() : default);
            case "session.status_idle":
                return new SessionStopped(id, StopReasonOf(raw));
            case "session.status_terminated":
                return new SessionStopped(id, StopReasons.Terminated);
            default:
                return null;
        }
    }

    /// <summary>
    ///     Motivo de um <c>session.status_idle</c>. A documentação chama o campo de <c>stop_reason</c> e o SDK 12.53 o
    ///     expõe como <c>stop_details</c>; aceita os dois e, sem nenhum, assume <see cref="StopReasons.EndTurn" />.
    /// </summary>
    private static string StopReasonOf(JsonElement raw) {
        foreach (var field in new[] { "stop_reason", "stop_details" }) {
            if (raw.TryGetProperty(field, out var stop) && stop.ValueKind == JsonValueKind.Object && stop.TryGetProperty("type", out var type)) {
                return type.GetString() ?? StopReasons.Error;
            }
        }

        return StopReasons.EndTurn;
    }

    private static IReadOnlyDictionary<string, JsonElement> Body(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
