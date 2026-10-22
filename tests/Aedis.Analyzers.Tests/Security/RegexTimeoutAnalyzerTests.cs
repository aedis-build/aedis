using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0101: Regex sem timeout é reportado; com timeout ou NonBacktracking, não.</summary>
public sealed class RegexTimeoutAnalyzerTests
{
    [Fact]
    public async Task Construtor_e_metodos_estaticos_sem_timeout_reportam()
    {
        const string source = """
            using System.Text.RegularExpressions;
            public class C {
                public bool M(string input) {
                    var r = new Regex("(a+)+$");
                    return Regex.IsMatch(input, "(a+)+$") || r.IsMatch(input);
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new RegexTimeoutAnalyzer(), source);

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "AEDIS0101");
    }

    [Fact]
    public async Task Com_timeout_ou_non_backtracking_nao_reporta()
    {
        const string source = """
            using System;
            using System.Text.RegularExpressions;
            public class C {
                public bool M(string input) {
                    var r = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                    var n = new Regex("(a+)+$", RegexOptions.NonBacktracking);
                    return Regex.IsMatch(input, "x", RegexOptions.None, TimeSpan.FromSeconds(1)) || r.IsMatch(input) || n.IsMatch(input);
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new RegexTimeoutAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
