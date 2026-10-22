using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0104: MD5, SHA-1, DES e ECB são reportados; SHA-256 e AES-GCM, não.</summary>
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
    public async Task Algoritmos_fortes_nao_reportam()
    {
        const string source = """
            using System.Security.Cryptography;
            public class C {
                public void M(byte[] data, byte[] key) {
                    var h = SHA256.HashData(data);
                    using var gcm = new AesGcm(key, 16);
                    using var aes = Aes.Create();
                    aes.Mode = CipherMode.CBC;
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new WeakCryptographyAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
