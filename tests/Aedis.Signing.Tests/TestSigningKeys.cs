using System.Security.Cryptography;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aedis.Signing.Tests;

internal static class TestSigningKeys
{
    public static ECDsa NewP256() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static SigningKeyHandle Handle(ECDsa key, string kid = "kid-1", string? reference = "vault://keys/kid-1") {
        var parameters = key.ExportParameters(false);
        return new SigningKeyHandle(kid, reference, SigningOptions.Algorithm, parameters, JsonWebKeyDocument.FromEcPublicKey(parameters, kid));
    }

    public static async Task<(InProcessSigningKeyProvider Provider, SigningKeyState State)> ReadyInProcessAsync(ECDsa key, string kid = "kid-1") {
        var state = new SigningKeyState();
        var provider = new InProcessSigningKeyProvider(key, kid, state, NullLogger<InProcessSigningKeyProvider>.Instance);
        await provider.EnsureKeyAsync();
        return (provider, state);
    }
}
