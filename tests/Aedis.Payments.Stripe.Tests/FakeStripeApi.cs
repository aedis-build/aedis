using System.Net;
using System.Text;
using Stripe;

namespace Aedis.Payments.Stripe.Tests;

/// <summary>
///     Fake da API da Stripe no nível HTTP: responde por <c>MÉTODO caminho</c> com o JSON enfileirado e grava cada
///     requisição (corpo de formulário decodificado e chave de idempotência) para as asserções.
/// </summary>
internal sealed class FakeStripeApi : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Json)>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeStripeApi Respond(string route, string json, HttpStatusCode status = HttpStatusCode.OK) {
        if (!_responses.TryGetValue(route, out var queue)) _responses[route] = queue = new Queue<(HttpStatusCode, string)>();
        queue.Enqueue((status, json));
        return this;
    }

    public StripeAccounts Accounts(StripeOptions options) =>
        new(options, new SystemNetHttpClient(new HttpClient(this), 0, null, false));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.Method == HttpMethod.Get) body = request.RequestUri.Query.TrimStart('?');
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
        request.Headers.TryGetValues("Idempotency-Key", out var keys);
        Requests.Add(new RecordedRequest(route, form, keys?.FirstOrDefault(), request.Headers.Authorization?.Parameter));

        if (!_responses.TryGetValue(route, out var queue) || queue.Count == 0)
            throw new InvalidOperationException($"Sem resposta fake para {route}.");
        var (status, json) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public sealed record RecordedRequest(string Route, IReadOnlyDictionary<string, string> Form, string? IdempotencyKey, string? ApiKey);
}
