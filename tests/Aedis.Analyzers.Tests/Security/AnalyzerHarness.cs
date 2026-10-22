using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aedis.Analyzers.Tests.Security;

/// <summary>
///     Compila um trecho de C# contra os assemblies do runtime de teste e devolve só os diagnósticos do analyzer
///     sob teste. Usa a lista de assemblies confiáveis do runtime para que Regex, criptografia e LINQ resolvam.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

    internal static async Task<ImmutableArray<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);
        var compilation = CSharpCompilation.Create(
            "Acme.Sample",
            new[] { CSharpSyntaxTree.ParseText(source) },
            References,
            options);

        var compileErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (compileErrors.Count > 0)
        {
            throw new InvalidOperationException("Fixture does not compile: " + string.Join("; ", compileErrors));
        }

        var ids = analyzer.SupportedDiagnostics.Select(descriptor => descriptor.Id).ToHashSet();
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(diagnostic => ids.Contains(diagnostic.Id)).ToImmutableArray();
    }
}
