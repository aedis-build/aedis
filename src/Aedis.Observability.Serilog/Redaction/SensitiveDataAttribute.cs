namespace Aedis.Observability.Serilog;

/// <summary>
///     Marca uma propriedade como dado sensível, para ser ofuscada ao ser logada (mesmo quando o nome do campo
///     não bate na heurística por nome). Use em campos ambíguos — por exemplo, um <c>Name</c> que de fato é
///     nome de pessoa, ou um <c>Address</c>. A ofuscação ocorre na destruturação do objeto (<c>{@obj}</c>),
///     antes de qualquer sink.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class SensitiveDataAttribute : Attribute {
    /// <summary>
    ///     Marca o campo como sensível, opcionalmente forçando uma estratégia específica.
    /// </summary>
    /// <param name="strategy">
    ///     Estratégia a aplicar; <see cref="RedactionStrategy.Inherit" /> (padrão) usa a estratégia de PII
    ///     configurada.
    /// </param>
    public SensitiveDataAttribute(RedactionStrategy strategy = RedactionStrategy.Inherit) {
        Strategy = strategy;
    }

    /// <summary>
    ///     Estratégia de ofuscação deste campo.
    /// </summary>
    public RedactionStrategy Strategy { get; }
}
