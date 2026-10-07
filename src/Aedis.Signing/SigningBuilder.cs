using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aedis.Signing;

/// <summary>
///     Ajustes fluentes após <c>AddAedisSigning</c>. Pacotes de provider estendem este builder (ex.:
///     <c>WithAwsKms()</c>) registrando <see cref="ISigningKeyProvider" /> e <see cref="ISignatureProvider" />
///     com chave igual ao nome do provider e fixando <see cref="SigningOptions.Provider" />.
/// </summary>
public sealed class SigningBuilder(IServiceCollection services)
{
    /// <summary>Coleção de serviços em configuração.</summary>
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>Força o provider <paramref name="providerName" />, sobrepondo <c>Signing:Provider</c>.</summary>
    public SigningBuilder UseProvider(string providerName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        Services.PostConfigure<SigningOptions>(o => o.Provider = providerName);
        return this;
    }

    /// <summary>Força chave em processo lida da configuração (<c>PrivateKeyPem</c> ou efêmera).</summary>
    public SigningBuilder WithInProcessKey() {
        Services.PostConfigure<SigningOptions>(o => {
            o.Provider = SigningKeyProviders.InProcess;
            o.InProcess.AllowEphemeralKey = o.InProcess.AllowEphemeralKey || string.IsNullOrWhiteSpace(o.InProcess.PrivateKeyPem);
        });
        return this;
    }

    /// <summary>Força uma chave ECDSA P-256 já construída (testes). A <paramref name="key" /> não é descartada pelo contêiner.</summary>
    public SigningBuilder WithInProcessKey(ECDsa key, string? keyId = null) {
        ArgumentNullException.ThrowIfNull(key);

        Services.PostConfigure<SigningOptions>(o => {
            o.Provider = SigningKeyProviders.InProcess;
            o.InProcess.AllowEphemeralKey = true;
            o.InProcess.KeyId = keyId ?? o.InProcess.KeyId;
        });
        Services.Replace(ServiceDescriptor.Singleton(sp => new InProcessSigningKeyProvider(
            key, keyId, sp.GetRequiredService<SigningKeyState>(), sp.GetRequiredService<ILogger<InProcessSigningKeyProvider>>())));
        return this;
    }
}
