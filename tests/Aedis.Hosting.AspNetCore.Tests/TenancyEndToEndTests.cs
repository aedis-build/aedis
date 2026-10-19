using System.Net;
using System.Net.Http.Json;
using Aedis.Diagnostics;
using Aedis.Tenancy.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     <c>EnableTenancy</c> no <see cref="AedisApiHost" />: o host registra a tenancy, o middleware resolve o
///     tenant de cada requisição com o <see cref="ITenantResolver" /> da aplicação e o endpoint o enxerga em
///     <see cref="ICurrentTenant" />; sem tenant, <see cref="ICurrentTenant.RequiredId" /> vira 403 ProblemDetails.
/// </summary>
public sealed class TenancyEndToEndTests
{
    private static async Task<WebApplication> StartAsync() {
        var app = new TenancySampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Endpoint_enxerga_o_tenant_resolvido_da_requisicao() {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Add(HeaderTenantResolver.Header, "acme");

        var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TenantResponse>())!.Tenant.Should().Be("acme");
    }

    [Fact]
    public async Task Sem_tenant_o_acesso_escopado_falha_fechado_com_403() {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().GetAsync("/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task O_tenant_nao_sobrevive_ao_fim_da_requisicao() {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Add(HeaderTenantResolver.Header, "acme");
        await app.GetTestClient().SendAsync(request);

        app.Services.GetRequiredService<ICurrentTenant>().Id.Should().BeNull();
    }

    private sealed record TenantResponse(string Tenant);

    private sealed class TenancySampleApiHost : AedisApiHost
    {
        protected override bool EnableAuthentication => false;

        protected override bool EnableTelemetry => false;

        protected override bool EnableTenancy => true;

        protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantResolver, HeaderTenantResolver>();
            services.Configure<GracefulShutdownOptions>(options => options.DrainDelay = TimeSpan.Zero);
        }

        protected override void ConfigureMiddleware(WebApplication app) =>
            app.MapGet("/tenant", (ICurrentTenant tenant) => Results.Ok(new { tenant = tenant.RequiredId }));
    }

    private sealed class HeaderTenantResolver(IHttpContextAccessor accessor) : ITenantResolver
    {
        public const string Header = "X-Test-Tenant";

        public ValueTask<string?> ResolveAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<string?>(accessor.HttpContext?.Request.Headers[Header].FirstOrDefault());
    }
}
