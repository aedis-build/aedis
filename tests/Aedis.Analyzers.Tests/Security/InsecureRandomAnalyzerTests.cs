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
    public async Task Random_em_uso_comum_nao_reporta()
    {
        const string source = """
            using System;
            public class C {
                public int Jitter() => Random.Shared.Next(0, 250);
                public void Shuffle(int[] items) {
                    var delay = Random.Shared.Next(10);
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new InsecureRandomAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
