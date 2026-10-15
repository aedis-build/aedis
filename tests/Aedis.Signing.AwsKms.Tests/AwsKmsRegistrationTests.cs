using Aedis.Signing.Abstractions;
using Aedis.Signing.AwsKms;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Signing.AwsKms.Tests;

/// <summary>
///     <c>AddAedisSigning(...).WithAwsKms(...)</c>: fixa o provider, deriva o alias padrão a partir do nome da
///     aplicação, herda região/endpoint da seção <c>Aws</c>, expõe os provedores pela costura e valida o alias.
///     Nada toca a rede — o cliente KMS é preguiçoso.
/// </summary>
public sealed class AwsKmsRegistrationTests
{
    private static ServiceProvider Build(params (string Key, string? Value)[] pairs) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value)).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAedisSigning(config).WithAwsKms(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Fixa_o_provider_e_deriva_alias_regiao_e_descricao() {
        using var provider = Build(("Signing:ApplicationName", "loja-pedidos"), ("Aws:Region", "sa-east-1"));

        var signing = provider.GetRequiredService<SigningOptions>();
        var kms = provider.GetRequiredService<AwsKmsSigningOptions>();

        signing.Provider.Should().Be("AwsKms");
        kms.KeyAlias.Should().Be("alias/aedis/loja-pedidos/signing");
        kms.Region.Should().Be("sa-east-1");
        kms.Description.Should().Be("Signing key (loja-pedidos)");
    }

    [Fact]
    public void Expoe_os_provedores_do_kms_pela_costura() {
        using var provider = Build(("Aws:Region", "sa-east-1"));

        provider.GetRequiredService<ISigningKeyProvider>().Should().BeOfType<KmsSigningKeyProvider>();
        provider.GetRequiredService<ISignatureProvider>().Should().BeOfType<KmsSignatureProvider>();
        provider.GetRequiredService<ISigningKeyProbe>().Should().BeOfType<KmsSigningKeyProvider>();
        provider.GetRequiredService<IHttpMessageSigner>().Should().BeOfType<HttpMessageSigner>();
    }

    [Fact]
    public void Alias_sem_prefixo_falha_a_validacao() {
        using var provider = Build(("Signing:AwsKms:KeyAlias", "loja/signing"));

        var act = () => provider.GetRequiredService<IOptions<AwsKmsSigningOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*alias/*");
    }
}
