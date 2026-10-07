using System.Text.Json;
using Aedis.Commands.Abstractions;
using Aedis.Core.Utils;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante o wiring que o host faz por omissão e que antes faltava: o <c>ICommandExecutor</c> exigido por
///     <c>ApiControllerBase</c> fica resolvível, e as opções de JSON do framework (camelCase e o conversor de
///     enum amigável) chegam tanto ao MVC quanto às minimal APIs — o que também governa a serialização dos
///     ProblemDetails escritos com <c>WriteAsJsonAsync</c>.
/// </summary>
public sealed class HostWiringTests
{
    private static async Task<WebApplication> StartAsync() {
        var app = new SampleApiHost().BuildApplication(["--environment", "Development"], builder => {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:Https:EnableHttpsRedirection"] = "false"
            });
        });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task ICommandExecutor_e_resolvivel_no_host_padrao() {
        await using var app = await StartAsync();

        var executor = app.Services.GetService<ICommandExecutor>();

        executor.Should().NotBeNull("ApiControllerBase exige ICommandExecutor e o host deve registrá-lo");
    }

    [Fact]
    public async Task Opcoes_JSON_das_minimal_APIs_recebem_camelCase_e_conversor_de_enum() {
        await using var app = await StartAsync();

        var options = app.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;

        options.PropertyNamingPolicy.Should().BeSameAs(JsonNamingPolicy.CamelCase);
        options.Converters.OfType<FriendlyEnumConverterFactory>().Should().ContainSingle();
    }

    [Fact]
    public async Task Opcoes_JSON_do_MVC_recebem_camelCase_e_conversor_de_enum() {
        await using var app = await StartAsync();

        var options = app.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions;

        options.PropertyNamingPolicy.Should().BeSameAs(JsonNamingPolicy.CamelCase);
        options.Converters.OfType<FriendlyEnumConverterFactory>().Should().ContainSingle();
    }
}
