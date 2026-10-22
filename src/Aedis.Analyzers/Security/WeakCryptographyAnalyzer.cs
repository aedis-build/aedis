using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aedis.Analyzers.Security;

/// <summary>
///     AEDIS0104: uso de MD5, SHA-1, DES, TripleDES, RC2 ou HMAC sobre MD5/SHA-1 de
///     <c>System.Security.Cryptography</c>, e do modo <c>CipherMode.ECB</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WeakCryptographyAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> WeakTypes = ImmutableHashSet.Create(
        "MD5", "SHA1", "DES", "TripleDES", "RC2", "HMACMD5", "HMACSHA1",
        "MD5CryptoServiceProvider", "SHA1CryptoServiceProvider", "SHA1Managed",
        "DESCryptoServiceProvider", "TripleDESCryptoServiceProvider", "RC2CryptoServiceProvider");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SecurityDescriptors.WeakCryptography);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeUse, OperationKind.Invocation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeCipherMode, OperationKind.FieldReference);
    }

    private static void AnalyzeUse(OperationAnalysisContext context)
    {
        var type = context.Operation switch
        {
            IInvocationOperation { TargetMethod.IsStatic: true } invocation => invocation.TargetMethod.ContainingType,
            IObjectCreationOperation creation => creation.Type as INamedTypeSymbol,
            _ => null,
        };
        if (type is not null && WeakTypes.Contains(type.Name) && InCryptography(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.WeakCryptography, context.Operation.Syntax.GetLocation(), type.Name));
        }
    }

    private static void AnalyzeCipherMode(OperationAnalysisContext context)
    {
        var field = ((IFieldReferenceOperation)context.Operation).Field;
        if (field.Name == "ECB" && field.ContainingType is { Name: "CipherMode" } mode && InCryptography(mode))
        {
            context.ReportDiagnostic(Diagnostic.Create(SecurityDescriptors.WeakCryptography, context.Operation.Syntax.GetLocation(), "CipherMode.ECB"));
        }
    }

    private static bool InCryptography(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "System.Security.Cryptography";
}
