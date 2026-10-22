using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aedis.Analyzers.Security;

/// <summary>
///     AEDIS0105: bloco <c>unsafe</c> e tipo ou membro marcado <c>unsafe</c>. <c>stackalloc</c> em
///     <c>Span&lt;T&gt;</c> continua permitido: o runtime confere os limites, e o <c>stackalloc</c> de ponteiro só
///     existe dentro de um contexto <c>unsafe</c>, que já é reportado.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeCodeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SecurityDescriptors.UnsafeCode);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            node => Report(node, ((UnsafeStatementSyntax)node.Node).UnsafeKeyword.GetLocation(), "unsafe"),
            SyntaxKind.UnsafeStatement);

        context.RegisterSyntaxNodeAction(
            AnalyzeModifiers,
            SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration, SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration,
            SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration, SyntaxKind.OperatorDeclaration,
            SyntaxKind.IndexerDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.DelegateDeclaration);
    }

    private static void AnalyzeModifiers(SyntaxNodeAnalysisContext context)
    {
        var modifiers = context.Node switch
        {
            MemberDeclarationSyntax member => member.Modifiers,
            LocalFunctionStatementSyntax local => local.Modifiers,
            _ => default,
        };

        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.UnsafeKeyword))
            {
                Report(context, modifier.GetLocation(), "unsafe");
            }
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string construct) =>
        context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.UnsafeCode, location, construct));
}
