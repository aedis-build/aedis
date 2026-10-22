using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aedis.Analyzers.Security;

/// <summary>
///     AEDIS0102: método de <c>System.Random</c> cujo resultado vai para um símbolo, ou que roda dentro de um
///     membro, com nome de valor secreto (token, código, senha, nonce, salt, chave, PIN).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InsecureRandomAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SecurityDescriptors.InsecureRandom);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var random = start.Compilation.GetTypeByMetadataName("System.Random");
            if (random is null)
            {
                return;
            }

            start.RegisterOperationAction(op =>
            {
                var invocation = (IInvocationOperation)op.Operation;
                if (!SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, random))
                {
                    return;
                }

                var subject = SecretSubject(invocation, op.ContainingSymbol);
                if (subject is not null)
                {
                    op.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.InsecureRandom, invocation.Syntax.GetLocation(), subject));
                }
            }, OperationKind.Invocation);
        });
    }

    private static string? SecretSubject(IInvocationOperation invocation, ISymbol containing)
    {
        // the local, field or property that receives the value names its purpose better than the method does
        for (var parent = invocation.Parent; parent is not null && parent is not IBlockOperation; parent = parent.Parent)
        {
            var target = TargetName(parent);
            if (target is not null)
            {
                return SensitiveNames.LooksGenerated(target) ? target : null;
            }
        }

        return SensitiveNames.LooksGenerated(containing.Name) ? containing.Name : null;
    }

    private static string? TargetName(IOperation operation) => operation switch
    {
        IVariableDeclaratorOperation declarator => declarator.Symbol.Name,
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } => local.Local.Name,
        ISimpleAssignmentOperation { Target: IFieldReferenceOperation field } => field.Field.Name,
        ISimpleAssignmentOperation { Target: IPropertyReferenceOperation property } => property.Property.Name,
        _ => null,
    };
}
