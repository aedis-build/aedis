using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0103: comparação comum de assinatura, HMAC ou token é reportada; FixedTimeEquals e nulos, não.</summary>
public sealed class ConstantTimeComparisonAnalyzerTests
{
    [Fact]
    public async Task Igualdade_comum_em_segredo_reporta()
    {
        const string source = """
            using System.Linq;
            public class C {
                public bool A(string signature, string expected) => signature == expected;
                public bool B(byte[] hmac, byte[] computed) => hmac.SequenceEqual(computed);
                public bool D(string apiKey, string header) => string.Equals(apiKey, header);
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new ConstantTimeComparisonAnalyzer(), source);

        diagnostics.Should().HaveCount(3);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "AEDIS0103");
    }

    [Fact]
    public async Task Tempo_constante_nulo_ou_nome_comum_nao_reporta()
    {
        const string source = """
            using System.Security.Cryptography;
            public class C {
                public bool A(byte[] signature, byte[] expected) => CryptographicOperations.FixedTimeEquals(signature, expected);
                public bool B(string token) => token == null;
                public bool D(string status, string other) => status == other;
                public bool E(int hashCode, int other) => hashCode == other;
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new ConstantTimeComparisonAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
