using Aedis.Signing.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>
///     <c>AddAedisSigning</c>: options validadas fail-closed, provider em processo por padrão, hosted service e
///     health check só com a assinatura ligada, signer desligado explícito, nome da aplicação derivado sem o
///     sufixo de host, e erro claro quando o provider configurado não tem pacote registrado.
/// </summary>
public sealed class SigningRegistrationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) => new ConfigurationBuilder()
        .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value)).Build();

    private static ServiceProvider Build(IConfiguration config, Action<SigningBuilder>? configure = null) {
        var services = new ServiceCollection().AddLogging();
        var builder = services.AddAedisSigning(config);
        configure?.Invoke(builder);
        return services.BuildServiceProvider();
    }

    private static Task BootstrapAsync(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<SigningKeyStartupService>().Single().StartAsync(CancellationToken.None);

    [Fact]
    public async Task Chave_em_processo_efemera_assina_e_publica_jwks() {
        await using var provider = Build(Config(("Signing:InProcess:AllowEphemeralKey", "true")));

        await BootstrapAsync(provider);

        provider.GetRequiredService<IHttpMessageSigner>().Should().BeOfType<HttpMessageSigner>();
        provider.GetRequiredService<IJwksProvider>().TryGet(out var jwks).Should().BeTrue();
        jwks!.Keys.Should().ContainSingle().Which.Kid.Should().StartWith("dev-");
        provider.GetRequiredService<SigningKeyState>().IsReady.Should().BeTrue();
    }

    [Fact]
    public void Sem_pem_e_sem_efemera_falha_a_validacao() {
        using var provider = Build(Config());

        var act = () => provider.GetRequiredService<IOptions<SigningOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*AllowEphemeralKey*");
    }

    [Fact]
    public void Desligado_expoe_signer_desligado_sem_hosted_service_nem_health_check() {
        using var provider = Build(Config(("Signing:Enabled", "false")));

        provider.GetRequiredService<IHttpMessageSigner>().Should().BeSameAs(DisabledHttpMessageSigner.Instance);
        provider.GetServices<IHostedService>().OfType<SigningKeyStartupService>().Should().BeEmpty();
        provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations.Should().BeEmpty();
    }

    [Fact]
    public void Ligado_registra_health_check_signing_como_ready() {
        using var provider = Build(Config(("Signing:InProcess:AllowEphemeralKey", "true")));

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "signing").Subject;

        registration.Tags.Should().Contain("ready");
    }

    [Fact]
    public void Provider_sem_pacote_registrado_falha_com_mensagem_clara() {
        using var provider = Build(Config(("Signing:Provider", "AwsKms")));

        var act = () => provider.GetRequiredService<IHttpMessageSigner>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'AwsKms'*não registrado*");
    }

    [Fact]
    public async Task Builder_injeta_chave_pronta_com_kid_fixo() {
        using var key = TestSigningKeys.NewP256();
        await using var provider = Build(Config(), b => b.WithInProcessKey(key, "kid-fixo"));

        await BootstrapAsync(provider);

        (await provider.GetRequiredService<IJwksProvider>().GetAsync()).Keys.Single().Kid.Should().Be("kid-fixo");
    }

    [Theory]
    [InlineData("loja.pedidos.api", "loja-pedidos")]
    [InlineData("Loja_Pedidos_Worker", "loja-pedidos")]
    [InlineData("pedidos", "pedidos")]
    [InlineData("api", "api")]
    [InlineData("", "aedis")]
    public void Nome_da_aplicacao_derivado_sem_sufixo_de_host(string input, string esperado) {
        SigningServiceCollectionExtensions.DeriveApplicationName(input).Should().Be(esperado);
    }
}
