using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using Aedis.Core.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Aedis.Hosting.AspNetCore.ServerSentEvents;

/// <summary>
///     Escreve um stream <c>text/event-stream</c> com o <see cref="SseFormatter" /> da plataforma, intercalando
///     heartbeats e respeitando os limites de <see cref="ServerSentEventsOptions" />. Serve tanto a controllers
///     (<see cref="IActionResult" />) quanto a minimal APIs (<see cref="IResult" />).
/// </summary>
internal sealed class ServerSentEventsResult<T> : IResult, IActionResult
{
    private static readonly byte[] Heartbeat = ": heartbeat\n\n"u8.ToArray();

    private readonly string? _lastEventId;
    private readonly Func<string?, CancellationToken, IAsyncEnumerable<SseItem<T>>> _source;

    public ServerSentEventsResult(Func<string?, CancellationToken, IAsyncEnumerable<SseItem<T>>> source, string? lastEventId) {
        _source = source;
        _lastEventId = lastEventId;
    }

    public Task ExecuteResultAsync(ActionContext context) {
        return ExecuteAsync(context.HttpContext);
    }

    public async Task ExecuteAsync(HttpContext httpContext) {
        var services = httpContext.RequestServices;
        var options = services.GetService<IOptions<ServerSentEventsOptions>>()?.Value ?? new ServerSentEventsOptions();
        var json = services.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? SystemJsonOptionsFactory.Create();

        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache,no-store";
        response.Headers.Pragma = "no-cache";
        // Resposta já "codificada" faz o ResponseCompression passar direto, mesmo se o app incluir text/event-stream nos MIME comprimidos.
        response.Headers.ContentEncoding = "identity";
        // nginx (inclusive o ingress-nginx) respeita este header e não bufferiza o stream.
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        lifetime.CancelAfter(options.MaxConnectionDuration);
        var cancellationToken = lifetime.Token;

        await response.StartAsync(cancellationToken);

        using var heartbeat = new PeriodicTimer(options.HeartbeatInterval);
        var body = response.Body;
        var events = _source(_lastEventId, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var next = events.MoveNextAsync().AsTask();
        var tick = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();

        try {
            while (true) {
                if (await Task.WhenAny(next, tick) == tick) {
                    await tick;
                    await body.WriteAsync(Heartbeat, cancellationToken);
                    await body.FlushAsync(cancellationToken);
                    tick = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                    continue;
                }

                if (!await next)
                    break;

                await SseFormatter.WriteAsync(Single(events.Current), body, (item, writer) => Format(item, writer, json), cancellationToken);
                await body.FlushAsync(cancellationToken);
                next = events.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Cliente desconectou ou a duração máxima venceu: fim normal do stream, o cliente reconecta com Last-Event-ID.
        }
        finally {
            lifetime.Cancel();
            // O enumerador não pode ser descartado com MoveNextAsync pendente; a fonte deve honrar o token.
            await Task.WhenAll(next, tick).ContinueWith(static _ => { }, TaskScheduler.Default);
            await events.DisposeAsync();
        }
    }

    private static void Format(SseItem<T> item, IBufferWriter<byte> writer, JsonSerializerOptions json) {
        if (item.Data is string text) {
            Encoding.UTF8.GetBytes(text, writer);
            return;
        }

        using var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, item.Data, json);
    }

    private static async IAsyncEnumerable<SseItem<T>> Single(SseItem<T> item) {
        yield return item;
        await Task.CompletedTask;
    }
}
