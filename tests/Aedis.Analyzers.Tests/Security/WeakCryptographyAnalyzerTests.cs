using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0104: MD5, SHA-1, DES e ECB são reportados; SHA-256, AES-GCM e homônimos fora da criptografia, não.</summary>
public sealed class WeakCryptographyAnalyzerTests
{
    [Fact]
    public async Task Algoritmos_fracos_reportam()
    {
        const string source = """
            using System.Security.Cryptography;
            public class C {
                public void M(byte[] data) {
                    var a = MD5.HashData(data);
                    using var b = SHA1.Create();
                    using var d = DES.Create();
                    using var aes = Aes.Create();
                    aes.Mode = CipherMode.ECB;
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new WeakCryptographyAnalyzer(), source);

        diagnostics.Should().HaveCount(4);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "AEDIS0104");
    }

    [Fact]
    public async Task Hmac_fraco_por_construtor_reporta()
    {
        const string source = """
            using System.Security.Cryptography;
            public class C {
                public byte[] M(byte[] key, byte[] data) {
                    using var hmac = new HMACSHA1(key);
                    return hmac.ComputeHash(data);
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new WeakCryptographyAnalyzer(), source);

        diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task Algoritmos_fortes_e_homonimos_nao_reportam()
    {
        const string source = """
            using System.Security.Cryptography;
            namespace Acme {
                public static class MD5 { public static int HashData(byte[] data) => data.Length; }
                public enum Mode { ECB, CBC }
                public class C {
                    public void M(byte[] data, byte[] key) {
                        var h = SHA256.HashData(data);
                        var mine = Acme.MD5.HashData(data);
                        var mode = Mode.ECB;
                        using var gcm = new AesGcm(key, 16);
                        using var aes = Aes.Create();
                        aes.Mode = CipherMode.CBC;
                        var text = new System.Text.StringBuilder();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new WeakCryptographyAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
