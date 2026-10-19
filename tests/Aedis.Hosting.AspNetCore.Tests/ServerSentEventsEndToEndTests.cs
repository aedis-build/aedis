using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Aedis.Diagnostics;
using Aedis.Hosting.AspNetCore.ServerSentEvents;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Server-Sent Events pelo host seguro: ordem e ids dos eventos, retomada por <c>Last-Event-ID</c>, stream fora
///     da compressão, heartbeat, fim no disconnect do cliente e na duração máxima, e o portão fail-closed.
/// </summary>
public sealed class ServerSentEventsEndToEndTests
{
    private static async Task<WebApplication> StartAsync(bool authentication = false, string heartbeat = "00:00:30",
        string maxDuration = "00:05:00") {
        var app = new ServerSentEventsSampleApiHost(authentication).BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false",
                ["ServerSentEvents:HeartbeatInterval"] = heartbeat,
                ["ServerSentEvents:MaxConnectionDuration"] = maxDuration
            });
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<List<SseItem<string>>> ReadEventsAsync(HttpResponseMessage response) {
        var stream = await response.Content.ReadAsStreamAsync();
        var events = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
            events.Add(item);
        return events;
    }

    [Fact]
    public async Task Eventos_chegam_em_ordem_com_id_tipo_e_json_do_host_sem_compressao() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sse/numeros?ate=3");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        response.Content.Headers.ContentEncoding.Should().NotContain("gzip");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");

        var events = await ReadEventsAsync(response);
        events.Select(e => e.EventId).Should().Equal("1", "2", "3");
        events.Should().OnlyContain(e => e.EventType == "numero");
        events[0].Data.Should().Be("""{"valor":1}""");
    }

    [Fact]
    public async Task Last_Event_ID_retoma_depois_do_ultimo_evento_recebido() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sse/numeros?ate=4");
        request.Headers.Add("Last-Event-ID", "2");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        (await ReadEventsAsync(response)).Select(e => e.EventId).Should().Equal("3", "4");
    }

    [Fact]
    public async Task Last_Event_ID_grande_demais_vira_400() {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sse/numeros?ate=1");
        request.Headers.Add("Last-Event-ID", new string('9', ServerSentEventsControllerExtensions.MaxLastEventIdLength + 1));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Conexao_ociosa_recebe_heartbeat() {
        await using var app = await StartAsync(heartbeat: "00:00:00.050");
        var client = app.GetTestClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/v1/sse/lento", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        string? line;
        do {
            line = await reader.ReadLineAsync(timeout.Token);
        } while (line is not null && line != ": heartbeat");

        line.Should().Be(": heartbeat");
    }

    [Fact]
    public async Task Disconnect_do_cliente_cancela_a_fonte() {
        await using var app = await StartAsync();
        var probe = app.Services.GetRequiredService<ServerSentEventsProbe>();
        var client = app.GetTestClient();
        using var cancel = new CancellationTokenSource();

        var response = await client.GetAsync("/v1/sse/lento", HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
        await stream.ReadExactlyAsync(new byte[1], cancel.Token);
        await cancel.CancelAsync();
        response.Dispose();

        await probe.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Duracao_maxima_encerra_o_stream() {
        await using var app = await StartAsync(maxDuration: "00:00:00.200");
        var client = app.GetTestClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/v1/sse/lento", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);

        body.Should().Contain("data: aberto");
    }

    [Fact]
    public async Task Sem_autenticacao_o_portao_fail_closed_recusa_o_stream() {
        await using var app = await StartAsync(authentication: true);
        var client = app.GetTestClient();

        using var anonymous = await client.GetAsync("/v1/sse/numeros?ate=1");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sse/numeros?ate=1");
        request.Headers.Add(HeaderAuthenticationHandler.HeaderName, "ana");
        using var authenticated = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadEventsAsync(authenticated)).Should().ContainSingle();
    }
}

/// <summary>Host de exemplo com um controller SSE; a autenticação usa um header de teste quando ligada.</summary>
public sealed class ServerSentEventsSampleApiHost(bool authentication) : AedisApiHost
{
    /// <inheritdoc />
    protected override bool EnableAuthentication => authentication;

    /// <inheritdoc cref="SampleApiHost.EnableTelemetry" />
    protected override bool EnableTelemetry => false;

    /// <inheritdoc />
    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) {
        services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers().AddApplicationPart(typeof(ServerSentEventsSampleApiHost).Assembly);
        services.AddSingleton<ServerSentEventsProbe>();
        services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override void ConfigureMiddleware(WebApplication app) {
        app.MapControllers();
    }
}

/// <summary>Sinaliza quando a fonte do stream lento terminou (cliente saiu ou duração venceu).</summary>
public sealed class ServerSentEventsProbe
{
    /// <summary>Completa no <c>finally</c> da fonte.</summary>
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Payload JSON de exemplo.</summary>
public sealed record Numero(int Valor);

/// <summary>Controller SSE de exemplo.</summary>
[ApiController]
[Route("v1/sse")]
public sealed class ServerSentEventsSampleController(ServerSentEventsProbe probe) : ControllerBase
{
    /// <summary>Números de 1 a <paramref name="ate" />, retomando depois do <c>Last-Event-ID</c>.</summary>
    [HttpGet("numeros")]
    public IActionResult Numeros(int ate) {
        return this.ServerSentEvents((lastEventId, ct) => NumerosAsync(int.TryParse(lastEventId, out var id) ? id : 0, ate, ct));
    }

    /// <summary>Um evento e depois silêncio até o cliente sair ou a duração vencer.</summary>
    [HttpGet("lento")]
    public IActionResult Lento() {
        return this.ServerSentEvents<string>((_, ct) => LentoAsync(ct));
    }

    private static async IAsyncEnumerable<SseItem<Numero>> NumerosAsync(int depoisDe, int ate,
        [EnumeratorCancellation] CancellationToken ct) {
        for (var i = depoisDe + 1; i <= ate; i++) {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new SseItem<Numero>(new Numero(i), "numero") { EventId = i.ToString() };
        }
    }

    private async IAsyncEnumerable<SseItem<string>> LentoAsync([EnumeratorCancellation] CancellationToken ct) {
        try {
            yield return new SseItem<string>("aberto");
            await Task.Delay(Timeout.Infinite, ct);
        }
        finally {
            probe.Closed.TrySetResult();
        }
    }
}

/// <summary>Autentica quem manda o header <see cref="HeaderName" />; sem ele, não há usuário.</summary>
public sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Nome do esquema.</summary>
    public const string SchemeName = "Header";

    /// <summary>Header com o nome do usuário.</summary>
    public const string HeaderName = "X-Test-User";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Request.Headers.TryGetValue(HeaderName, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
