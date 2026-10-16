using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Signing.Tests;

/// <summary>Carga da chave em processo: PEM válido, PEM inválido, chave efêmera, curva errada e o builder.</summary>
public sealed class InProcessSigningKeyProviderTests
{
    private static InProcessSigningKeyProvider Provider(SigningOptions options, SigningKeyState? state = null) =>
        new(options, state ?? new SigningKeyState(), NullLogger<InProcessSigningKeyProvider>.Instance);

    [Fact]
    public async Task Pem_valido_e_carregado_e_assina_com_o_kid_configurado() {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var provider = Provider(new SigningOptions { InProcess = { PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(), KeyId = "k1" } });

        var handle = await provider.EnsureKeyAsync();
        var again = await provider.EnsureKeyAsync();
        var signature = await provider.SignDigestAsync(SHA256.HashData("x"u8));

        handle.KeyId.Should().Be("k1");
        again.Should().BeSameAs(handle);
        provider.Algorithm.Should().Be(SigningOptions.Algorithm);
        provider.TryGetKey(out var cached).Should().BeTrue();
        cached.Should().BeSameAs(handle);
        key.VerifyHash(SHA256.HashData("x"u8), signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();
    }

    [Fact]
    public async Task Pem_invalido_e_falta_de_pem_sem_efemera_falham_com_erros_distintos() {
        using var invalid = Provider(new SigningOptions { InProcess = { PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nxx\n-----END PRIVATE KEY-----" } });
        using var missing = Provider(new SigningOptions { InProcess = { AllowEphemeralKey = false } });

        var invalidAct = () => invalid.EnsureKeyAsync();
        var missingAct = () => missing.EnsureKeyAsync();

        await invalidAct.Should().ThrowAsync<SigningKeyInvalidStateException>().WithMessage("*PEM*");
        await missingAct.Should().ThrowAsync<SigningKeyNotFoundException>().WithMessage("*AllowEphemeralKey*");
    }

    [Fact]
    public async Task Chave_efemera_ganha_kid_derivado_do_thumbprint() {
        using var provider = Provider(new SigningOptions { InProcess = { AllowEphemeralKey = true } });

        var handle = await provider.EnsureKeyAsync();

        handle.KeyId.Should().StartWith("dev-");
    }

    [Fact]
    public async Task Chave_fora_da_curva_P256_e_rejeitada() {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var provider = new InProcessSigningKeyProvider(key, "k384", new SigningKeyState(),
            NullLogger<InProcessSigningKeyProvider>.Instance);

        var act = () => provider.EnsureKeyAsync();

        await act.Should().ThrowAsync<SigningKeyInvalidStateException>().WithMessage("*P-256*");
    }

    [Fact]
    public void Builder_configura_provider_e_chave_efemera() {
        var services = new ServiceCollection();
        new SigningBuilder(services).UseProvider("custom").WithInProcessKey();
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SigningOptions>>().Value;

        options.Provider.Should().Be(SigningKeyProviders.InProcess);
        options.InProcess.AllowEphemeralKey.Should().BeTrue("sem PEM configurado o builder libera a chave efêmera");

        var act = () => new SigningBuilder(services).UseProvider(" ");
        act.Should().Throw<ArgumentException>();
    }
}
