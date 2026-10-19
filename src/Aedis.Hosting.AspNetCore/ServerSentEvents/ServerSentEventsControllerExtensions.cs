using System.Net.ServerSentEvents;
using Aedis.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.Hosting.AspNetCore.ServerSentEvents;

/// <summary>
///     Server-Sent Events a partir de um controller: o stream passa pelo pipeline seguro do host (autenticação
///     fail-closed, headers de segurança, rate limit) e chega ao navegador sem compressão nem buffer, com
///     heartbeat, retomada por <c>Last-Event-ID</c>, fim no disconnect do cliente e duração máxima
///     (<see cref="ServerSentEventsOptions" />). Cada <see cref="SseItem{T}.Data" /> sai como JSON com as opções
///     do host (<c>SystemJsonOptionsFactory</c>); <c>string</c> sai como texto.
/// </summary>
/// <example>
///     <code>
///     [HttpGet("eventos")]
///     public IActionResult Eventos(long conversaId) {
///         return this.ServerSentEvents((lastEventId, ct) =&gt;
///             _mensagens.AcompanharAsync(conversaId, long.TryParse(lastEventId, out var id) ? id : 0, ct)
///                 .Select(m =&gt; new SseItem&lt;Mensagem&gt;(m, "mensagem") { EventId = m.Id.ToString() }));
///     }
///     </code>
/// </example>
public static class ServerSentEventsControllerExtensions
{
    /// <summary>Tamanho máximo aceito no header <c>Last-Event-ID</c>, que vem do cliente.</summary>
    public const int MaxLastEventIdLength = 256;

    /// <summary>
    ///     Abre um stream <c>text/event-stream</c> com os eventos produzidos por <paramref name="source" />. A fonte
    ///     recebe o <c>Last-Event-ID</c> enviado pelo navegador na reconexão (<c>null</c> na primeira conexão) para
    ///     retomar do ponto certo, e um token cancelado quando o cliente desconecta ou a duração máxima vence — a
    ///     fonte deve honrá-lo. Use <see cref="SseItem{T}.EventId" /> para que o cliente saiba de onde retomar.
    /// </summary>
    /// <typeparam name="T">Tipo do payload de cada evento.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="source">Fábrica do stream a partir do último id recebido pelo cliente.</param>
    /// <exception cref="BusinessException">
    ///     <c>Last-Event-ID</c> maior que <see cref="MaxLastEventIdLength" /> (400).
    /// </exception>
    public static IActionResult ServerSentEvents<T>(this ControllerBase controller,
        Func<string?, CancellationToken, IAsyncEnumerable<SseItem<T>>> source) {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(source);

        string? lastEventId = controller.Request.Headers["Last-Event-ID"];
        if (lastEventId?.Length > MaxLastEventIdLength)
            throw new BusinessException($"Last-Event-ID excede {MaxLastEventIdLength} caracteres.", ViolationType.ValidationError,
                "last-event-id-too-long", StatusCodes.Status400BadRequest);

        return new ServerSentEventsResult<T>(source, string.IsNullOrEmpty(lastEventId) ? null : lastEventId);
    }
}
