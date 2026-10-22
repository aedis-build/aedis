; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AEDIS0001 | Aedis.Architecture | Warning | DependencyRuleAnalyzer: regra de dependência entre camadas
AEDIS0101 | Aedis.Security | Warning | RegexTimeoutAnalyzer: Regex sem timeout (ReDoS, CWE-1333)
AEDIS0102 | Aedis.Security | Warning | InsecureRandomAnalyzer: System.Random gerando valor secreto (CWE-338)
AEDIS0103 | Aedis.Security | Warning | ConstantTimeComparisonAnalyzer: comparação de segredo sem tempo constante (CWE-208)
AEDIS0104 | Aedis.Security | Warning | WeakCryptographyAnalyzer: MD5, SHA-1, DES, 3DES, RC2 e ECB (CWE-327)
AEDIS0105 | Aedis.Security | Warning | UnsafeCodeAnalyzer: código unsafe e ponteiros (CWE-119)
