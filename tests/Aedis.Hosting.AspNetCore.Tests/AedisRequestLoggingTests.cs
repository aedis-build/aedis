using System.Security.Claims;
using Aedis.Hosting.AspNetCore.RequestLogging;
using Aedis.Observability.Serilog;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Serilog;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante que o enriquecimento do access-log é seguro: ofusca a query string, deriva o <c>UserId</c> do
///     token autenticado e <strong>nunca</strong> registra o header <c>Authorization</c>.
/// </summary>
public sealed class AedisRequestLoggingTests {
    [Fact]
    public void Enrich_ofusca_querystring_define_userid_e_nao_loga_authorization() {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.test");
        httpContext.Request.QueryString = new QueryString("?token=secretvalue&page=2");
        httpContext.Request.Headers.Authorization = "Bearer abc.def.ghi";
        httpContext.Request.Headers.UserAgent = "curl/8.0";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-123")], "test"));
        var diagnostic = new FakeDiagnosticContext();

        AedisRequestLogging.Enrich(diagnostic, httpContext, new RedactionOptions());

        diagnostic.Properties["QueryString"].Should().Be("?token=***&page=2");
        diagnostic.Properties["UserId"].Should().Be("user-123");
        diagnostic.Properties["UserAgent"].Should().Be("curl/8.0");
        diagnostic.Properties["Host"].Should().Be("api.test");
        diagnostic.Properties.Should().NotContainKey("Authorization");
    }

    [Fact]
    public void Enrich_sem_usuario_autenticado_nao_define_userid() {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");
        var diagnostic = new FakeDiagnosticContext();

        AedisRequestLogging.Enrich(diagnostic, httpContext, new RedactionOptions());

        diagnostic.Properties.Should().NotContainKey("UserId");
    }

    private sealed class FakeDiagnosticContext : IDiagnosticContext {
        public Dictionary<string, object?> Properties { get; } = [];

        public void Set(string propertyName, object? value, bool destructureObjects = false) {
            Properties[propertyName] = value;
        }

        public void SetException(Exception exception) {
        }
    }
}
