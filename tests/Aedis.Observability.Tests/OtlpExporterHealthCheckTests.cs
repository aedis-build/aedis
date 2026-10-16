using System.Net;
using Aedis.Observability.Otlp;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>Health check do exportador OTLP: sem endpoint é saudável; endpoint responde, falha ou estoura o tempo.</summary>
public sealed class OtlpExporterHealthCheckTests
{
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static OtlpExporterHealthCheck Create(string? endpoint, Func<HttpResponseMessage> respond) =>
        new(Options.Create(new TelemetryOptions { OtlpEndpoint = endpoint }), new Factory(new StubHandler(respond)));

    [Fact]
    public async Task Sem_endpoint_e_saudavel_sem_chamar_a_rede() {
        var result = await Create(null, () => throw new InvalidOperationException("não deveria chamar"))
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Endpoint_que_responde_e_saudavel_mesmo_com_erro_http() {
        var result = await Create("http://otel:4318/v1/traces", () => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed))
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("405");
    }

    [Fact]
    public async Task Falha_de_rede_e_timeout_sao_unhealthy() {
        var network = await Create("http://otel:4318", () => throw new HttpRequestException("recusada"))
            .CheckHealthAsync(new HealthCheckContext());
        var timeout = await Create("http://otel:4318", () => throw new TaskCanceledException("timeout"))
            .CheckHealthAsync(new HealthCheckContext());

        network.Status.Should().Be(HealthStatus.Unhealthy);
        network.Description.Should().Contain("recusada");
        timeout.Status.Should().Be(HealthStatus.Unhealthy);
        timeout.Description.Should().Contain("Timeout");
    }
}
