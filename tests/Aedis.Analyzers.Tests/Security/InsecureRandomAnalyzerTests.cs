using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0102: System.Random gerando valor com nome de segredo é reportado; uso comum, não.</summary>
public sealed class InsecureRandomAnalyzerTests
{
    [Fact]
    public async Task Random_em_valor_secreto_reporta()
    {
        const string source = """
            using System;
            public class C {
                public int GenerateCode() => Random.Shared.Next(100000, 999999);
                public void M() {
                    var resetToken = new Random().Next();
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new InsecureRandomAnalyzer(), source);

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "AEDIS0102");
    }

    [Fact]
    public async Task Atribuicao_a_local_campo_e_propriedade_secretos_reporta()
    {
        const string source = """
            using System;
            public class C {
                private int _nonce;
                public int Pin { get; set; }
                public void M() {
                    int otp;
                    otp = Random.Shared.Next();
                    _nonce = Random.Shared.Next();
                    Pin = Random.Shared.Next(1000, 9999);
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new InsecureRandomAnalyzer(), source);

        diagnostics.Should().HaveCount(3);
    }

    [Fact]
    public async Task Random_em_uso_comum_nao_reporta()
    {
        const string source = """
            using System;
            public class C {
                private int _jitter;
                public int Retry { get; set; }
                public int Jitter() => Random.Shared.Next(0, 250);
                public void Shuffle(int[] items) {
                    var delay = Random.Shared.Next(10);
                    int backoff;
                    backoff = Random.Shared.Next();
                    _jitter = Random.Shared.Next();
                    Retry = Random.Shared.Next();
                    Random.Shared.Shuffle(items);
                }
                public string Describe() => Guid.NewGuid().ToString();
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new InsecureRandomAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
