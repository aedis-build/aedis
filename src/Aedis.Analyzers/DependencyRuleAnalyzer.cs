using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aedis.Analyzers;

/// <summary>
/// Impõe a regra de dependência do Aedis em tempo de compilação: um assembly de camada de domínio
/// (<c>*.Domain</c>) ou de aplicação (<c>*.Application</c>) não deve <strong>usar</strong> tipos de uma
/// implementação concreta do Aedis (ex.: <c>Aedis.Database.Postgres</c>, <c>Aedis.Cache.Redis</c>,
/// <c>Aedis.Messaging.RabbitMq</c>, <c>Aedis.Hosting.AspNetCore</c>). Deve depender apenas de contratos
/// (<c>Aedis.Core</c>, <c>Aedis.Domain</c>, <c>Aedis.Exceptions</c>, <c>Aedis.Commands</c> e qualquer
/// <c>*.Abstractions</c>) — a fiação concreta pertence à composition root (<c>*.Api</c>/<c>*.Worker</c>).
/// A análise é por uso real de tipo/membro, então dependências transitivas não-usadas não geram ruído.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyRuleAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Identificador do diagnóstico emitido por este analyzer ("AEDIS0001").</summary>
    public const string DiagnosticId = "AEDIS0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Regra de dependência do Aedis",
        messageFormat: "O assembly '{0}' ({1}) usa a implementação '{2}'; dependa de contratos (Aedis.Core, Aedis.Domain, Aedis.Exceptions, Aedis.Commands, *.Abstractions) — a fiação concreta pertence à composition root (*.Api/*.Worker)",
        category: "Aedis.Architecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Código de domínio/aplicação usa apenas contratos Aedis; implementações concretas entram na composition root. Ajuste a severidade por .editorconfig (dotnet_diagnostic.AEDIS0001.severity).");

    /// <summary>Descritores de diagnóstico suportados por este analyzer (a regra de dependência do Aedis).</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>
    ///     Configura o analyzer no pipeline do Roslyn: ignora código gerado, habilita execução concorrente e —
    ///     apenas em assemblies de domínio/aplicação — registra a análise de uso de tipos/membros.
    /// </summary>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var assemblyName = context.Compilation.AssemblyName;
        if (string.IsNullOrEmpty(assemblyName))
        {
            return;
        }

        var layer = RestrictedLayer(assemblyName!);
        if (layer is null)
        {
            return;
        }

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeUsage(nodeContext, assemblyName!, layer),
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName);
    }

    private static void AnalyzeUsage(SyntaxNodeAnalysisContext context, string assemblyName, string layer)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null or INamespaceSymbol)
        {
            return;
        }

        var assembly = symbol.ContainingAssembly;
        if (assembly is null)
        {
            return;
        }

        if (IsRestrictedImplementation(assembly.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), assemblyName, layer, assembly.Name));
        }
    }

    private static string? RestrictedLayer(string assemblyName)
    {
        if (EndsWithSegment(assemblyName, "Domain"))
        {
            return "Domain";
        }

        return EndsWithSegment(assemblyName, "Application") ? "Application" : null;
    }

    private static bool EndsWithSegment(string assemblyName, string segment) =>
        assemblyName.Equals(segment, StringComparison.OrdinalIgnoreCase)
        || assemblyName.EndsWith("." + segment, StringComparison.OrdinalIgnoreCase);

    private static bool IsRestrictedImplementation(string referenceName)
    {
        if (!referenceName.StartsWith("Aedis.", StringComparison.Ordinal))
        {
            return false;
        }

        if (referenceName.EndsWith(".Abstractions", StringComparison.Ordinal))
        {
            return false;
        }

        switch (referenceName)
        {
            case "Aedis.Core":
            case "Aedis.Domain":
            case "Aedis.Exceptions":
            case "Aedis.Commands":
                return false;
            default:
                return true;
        }
    }
}
