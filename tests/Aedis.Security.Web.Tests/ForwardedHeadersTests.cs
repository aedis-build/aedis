using Aedis.Security.Web.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Security.Web.Tests;

/// <summary>
///     Cabeçalhos encaminhados: <c>TrustAllProxies</c> só vale sem proxies/redes conhecidos; informar
///     <c>KnownNetworks</c> restringe mesmo com <c>TrustAllProxies=true</c> esquecido (spoofing de
///     <c>X-Forwarded-For</c> fica barrado).
/// </summary>
public sealed class ForwardedHeadersTests
{
    [Fact]
    public void Sem_redes_conhecidas_confia_em_qualquer_proxy() {
        new ForwardedHeadersHardeningOptions().EffectivelyTrustsAllProxies.Should().BeTrue();
    }

    [Fact]
    public void Redes_conhecidas_anulam_TrustAllProxies() {
        var options = new ForwardedHeadersHardeningOptions { TrustAllProxies = true };
        options.KnownNetworks.Add("10.0.0.0/8");

        options.EffectivelyTrustsAllProxies.Should().BeFalse();
    }

    [Fact]
    public void KnownNetworks_na_configuracao_viram_redes_do_ForwardedHeadersOptions() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:ForwardedHeaders:TrustAllProxies"] = "true",
            ["Security:ForwardedHeaders:KnownNetworks:0"] = "10.64.0.0/16"
        }).Build();
        var provider = new ServiceCollection().AddLogging().AddAedisWebSecurity(configuration).BuildServiceProvider();

        var forwarded = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        forwarded.KnownIPNetworks.Should().Contain(n => n.BaseAddress.ToString() == "10.64.0.0");
    }
}
