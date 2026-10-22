using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aedis.Analyzers.Security;

/// <summary>
///     AEDIS0103: <c>==</c>, <c>!=</c>, <c>Equals</c> ou <c>SequenceEqual</c> sobre texto ou bytes cujo nome
///     indica assinatura, HMAC, hash, token ou chave. Essas operações terminam no primeiro byte diferente.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstantTimeComparisonAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SecurityDescriptors.NonConstantTimeComparison);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeBinary, OperationKind.Binary);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeBinary(OperationAnalysisContext context)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var operands = new[] { binary.LeftOperand, binary.RightOperand };
        if (Qualifies(operands))
        {
            var symbol = binary.OperatorKind == BinaryOperatorKind.Equals ? "==" : "!=";
            context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.NonConstantTimeComparison, binary.Syntax.GetLocation(), symbol));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var name = invocation.TargetMethod.Name;
        if (name is not ("Equals" or "SequenceEqual"))
        {
            return;
        }

        var operands = new List<IOperation>(invocation.Arguments.Select(argument => argument.Value));
        if (invocation.Instance is not null)
        {
            operands.Add(invocation.Instance);
        }

        if (operands.Count >= 2 && Qualifies(operands))
        {
            context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.NonConstantTimeComparison, invocation.Syntax.GetLocation(), name));
        }
    }

    private static bool Qualifies(IReadOnlyCollection<IOperation> operands) =>
        operands.All(operand => IsComparable(Unwrap(operand).Type))
        && !operands.Any(IsNullOrDefault)
        && operands.Any(Sensitive);

    private static IOperation Unwrap(IOperation operand) => operand is IConversionOperation conversion ? conversion.Operand : operand;

    private static bool IsComparable(ITypeSymbol? type) =>
        type is not null
        && (type.SpecialType == SpecialType.System_String
            || type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte }
            || type.Name is "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory");

    private static bool IsNullOrDefault(IOperation operand)
    {
        var value = Unwrap(operand);
        return (value.ConstantValue.HasValue && value.ConstantValue.Value is null) || value is IDefaultValueOperation;
    }

    private static bool Sensitive(IOperation operand)
    {
        var name = Unwrap(operand) switch
        {
            ILocalReferenceOperation local => local.Local.Name,
            IParameterReferenceOperation parameter => parameter.Parameter.Name,
            IFieldReferenceOperation field => field.Field.Name,
            IPropertyReferenceOperation property => property.Property.Name,
            IInvocationOperation call => call.TargetMethod.Name,
            _ => null,
        };
        return SensitiveNames.LooksCompared(name);
    }
}
