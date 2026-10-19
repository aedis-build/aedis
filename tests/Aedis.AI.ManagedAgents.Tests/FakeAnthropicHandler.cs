using System.Net;
using System.Text;
using Anthropic;

namespace Aedis.AI.ManagedAgents.Tests;

/// <summary>
///     API do Managed Agents em memória: responde por rota (<c>MÉTODO /caminho</c>, sem query) com respostas
///     enfileiradas e guarda cada requisição com o corpo, para os testes conferirem o que o motor enviou.
/// </summary>
internal sealed class FakeAnthropicHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _routes = new();

    public List<(string Route, string Query, string Body)> Requests { get; } = [];

    public FakeAnthropicHandler On(string route, Func<HttpResponseMessage> response) {
        if (!_routes.TryGetValue(route, out var queue)) _routes[route] = queue = new Queue<Func<HttpResponseMessage>>();
        queue.Enqueue(response);
        return this;
    }

    public FakeAnthropicHandler OnJson(string route, string json) =>
        On(route, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public FakeAnthropicHandler OnStream(string route, params string[] events) =>
        On(route, () => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(string.Concat(events.Select(e => $"event: message\ndata: {e}\n\n")), Encoding.UTF8, "text/event-stream")
        });

    public AnthropicClient Client() => new() { ApiKey = "sk-teste", HttpClient = new HttpClient(this), MaxRetries = 0 };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((route, request.RequestUri.Query, body));
        if (!_routes.TryGetValue(route, out var queue) || queue.Count == 0)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"" + route + "\"}}", Encoding.UTF8, "application/json") };

        var factory = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return factory();
    }
}
