using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aedis.Analyzers.Security;

/// <summary>
///     AEDIS0101: construtor e métodos estáticos de <c>System.Text.RegularExpressions.Regex</c> chamados sem
///     <c>matchTimeout</c> e sem <c>RegexOptions.NonBacktracking</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegexTimeoutAnalyzer : DiagnosticAnalyzer
{
    private const int NonBacktracking = 1024;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SecurityDescriptors.RegexWithoutTimeout);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var regex = start.Compilation.GetTypeByMetadataName("System.Text.RegularExpressions.Regex");
            var timeSpan = start.Compilation.GetTypeByMetadataName("System.TimeSpan");
            if (regex is null || timeSpan is null)
            {
                return;
            }

            start.RegisterOperationAction(op =>
            {
                var creation = (IObjectCreationOperation)op.Operation;
                Analyze(op, creation.Constructor, creation.Arguments, regex, timeSpan);
            }, OperationKind.ObjectCreation);

            start.RegisterOperationAction(op =>
            {
                var invocation = (IInvocationOperation)op.Operation;
                if (invocation.TargetMethod.IsStatic)
                {
                    Analyze(op, invocation.TargetMethod, invocation.Arguments, regex, timeSpan);
                }
            }, OperationKind.Invocation);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        IMethodSymbol? method,
        ImmutableArray<IArgumentOperation> arguments,
        INamedTypeSymbol regex,
        INamedTypeSymbol timeSpan)
    {
        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, regex))
        {
            return;
        }

        if (method.MethodKind != MethodKind.Constructor && !IsMatchingMethod(method.Name))
        {
            return;
        }

        if (method.Parameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, timeSpan)))
        {
            return;
        }

        if (arguments.Any(UsesNonBacktracking))
        {
            return;
        }

        var name = method.MethodKind == MethodKind.Constructor ? "new Regex(...)" : "Regex." + method.Name;
        context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.RegexWithoutTimeout, context.Operation.Syntax.GetLocation(), name));
    }

    private static bool UsesNonBacktracking(IArgumentOperation argument) =>
        argument.Parameter?.Name == "options"
        && argument.Value.ConstantValue.HasValue
        && argument.Value.ConstantValue.Value is int flags
        && (flags & NonBacktracking) != 0;

    private static bool IsMatchingMethod(string name) =>
        name is "IsMatch" or "Match" or "Matches" or "Replace" or "Split" or "Count" or "EnumerateMatches" or "EnumerateSplits";
}
