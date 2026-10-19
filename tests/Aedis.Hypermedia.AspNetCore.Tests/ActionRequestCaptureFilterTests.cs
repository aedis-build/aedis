using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Aedis.Hypermedia.AspNetCore.Tests;

/// <summary>
///     O filtro captura exatamente um argumento complexo vindo do corpo ou da query string; com dois candidatos,
///     ou só tipos simples/coleções/rota, não captura nada.
/// </summary>
public sealed class ActionRequestCaptureFilterTests {
    private sealed record SearchRequest(string? Name);

    private sealed record OtherRequest(int Value);

    private static (ActionExecutingContext Context, HttpContext Http) Build(params (string Name, object? Value, BindingSource? Source)[] arguments) {
        var http = new DefaultHttpContext();
        var descriptor = new ActionDescriptor {
            Parameters = arguments.Select(a => (ParameterDescriptor)new ParameterDescriptor {
                Name = a.Name,
                ParameterType = a.Value?.GetType() ?? typeof(object),
                BindingInfo = a.Source is null ? null : new BindingInfo { BindingSource = a.Source }
            }).ToList()
        };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var context = new ActionExecutingContext(actionContext, [], arguments.ToDictionary(a => a.Name, a => a.Value), new object());
        return (context, http);
    }

    [Fact]
    public void Captura_o_unico_argumento_complexo_do_corpo() {
        var (context, http) = Build(("request", new SearchRequest("x"), BindingSource.Body), ("ct", CancellationToken.None, null));

        new ActionRequestCaptureFilter().OnActionExecuting(context);

        http.GetCapturedActionRequest().Should().BeOfType<SearchRequest>().Which.Name.Should().Be("x");
    }

    [Fact]
    public void Captura_argumento_complexo_ligado_por_convencao_na_query() {
        var (context, http) = Build(("request", new SearchRequest("y"), null));

        new ActionRequestCaptureFilter().OnActionExecuting(context);

        http.GetCapturedActionRequest().Should().NotBeNull();
    }

    [Fact]
    public void Dois_candidatos_nao_capturam_nada() {
        var (context, http) = Build(("a", new SearchRequest("x"), BindingSource.Body), ("b", new OtherRequest(1), BindingSource.Query));

        new ActionRequestCaptureFilter().OnActionExecuting(context);

        http.GetCapturedActionRequest().Should().BeNull();
    }

    [Fact]
    public void Tipos_simples_colecoes_e_rota_sao_ignorados() {
        var (context, http) = Build(
            ("id", 42, BindingSource.Path),
            ("name", "x", BindingSource.Query),
            ("items", new List<SearchRequest>(), BindingSource.Body),
            ("fromRoute", new SearchRequest("r"), BindingSource.Path));

        new ActionRequestCaptureFilter().OnActionExecuting(context);

        http.GetCapturedActionRequest().Should().BeNull();
    }
}
