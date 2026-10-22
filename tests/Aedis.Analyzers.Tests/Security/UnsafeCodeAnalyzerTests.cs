using System.Threading.Tasks;
using Aedis.Analyzers.Security;
using FluentAssertions;
using Xunit;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>AEDIS0105: bloco e membro unsafe são reportados; código gerenciado e stackalloc em Span, não.</summary>
public sealed class UnsafeCodeAnalyzerTests
{
    [Fact]
    public async Task Bloco_e_membro_unsafe_reportam()
    {
        const string source = """
            using System;
            public class C {
                public unsafe void A(int* p) { }
                public void B() {
                    unsafe { int x = 0; int* p = &x; }
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new UnsafeCodeAnalyzer(), source);

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().OnlyContain(diagnostic => diagnostic.Id == "AEDIS0105");
    }

    [Fact]
    public async Task Codigo_gerenciado_e_stackalloc_em_span_nao_reportam()
    {
        const string source = """
            using System;
            public class C {
                public void M() {
                    Span<byte> heap = new byte[16];
                    Span<char> stack = stackalloc char[16];
                    heap.Fill(1);
                    stack.Fill('a');
                }
            }
            """;

        var diagnostics = await AnalyzerHarness.RunAsync(new UnsafeCodeAnalyzer(), source);

        diagnostics.Should().BeEmpty();
    }
}
