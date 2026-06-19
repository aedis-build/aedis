using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aedis.Analyzers;

/// <summary>
/// Placeholder do analyzer que imporá a regra de dependência do Aedis
/// (código de domínio/aplicação não pode referenciar pacotes de implementação).
/// A regra concreta será implementada na decomposição — ver MIGRATION.md.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyRuleAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Identificador do diagnóstico emitido por este analyzer ("AEDIS0001").</summary>
    public const string DiagnosticId = "AEDIS0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Regra de dependência do Aedis",
        messageFormat: "Domínio/aplicação não deve referenciar implementações concretas ('{0}')",
        category: "Aedis.Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Código de domínio e aplicação referencia apenas Aedis.Core e *.Abstractions.");

    /// <summary>Descritores de diagnóstico suportados por este analyzer (a regra de dependência do Aedis).</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>
    ///     Configura o analyzer no pipeline do Roslyn: ignora código gerado e habilita execução concorrente.
    ///     O registro da análise concreta da regra ainda está pendente (ver TODO interno e MIGRATION.md).
    /// </summary>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // TODO: registrar a análise concreta na fase de decomposição (WS3).
    }
}
