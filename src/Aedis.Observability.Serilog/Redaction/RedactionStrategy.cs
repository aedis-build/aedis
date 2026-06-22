namespace Aedis.Observability.Serilog;

/// <summary>
///     Estratégia de ofuscação aplicada a um valor sensível antes de ele chegar a qualquer sink.
/// </summary>
public enum RedactionStrategy {
    /// <summary>
    ///     Usa a estratégia padrão configurada (de PII). Válido apenas em <see cref="SensitiveDataAttribute" />;
    ///     nas opções, trate como <see cref="Partial" />.
    /// </summary>
    Inherit = 0,

    /// <summary>
    ///     Substitui o valor inteiro pelo placeholder (por exemplo, <c>***</c>). Sem vazamento parcial — é a
    ///     escolha segura para segredos.
    /// </summary>
    Mask,

    /// <summary>
    ///     Mantém apenas os últimos caracteres do valor (por exemplo, <c>***6789</c>). Útil para correlação em
    ///     suporte; use só em PII, nunca em segredos.
    /// </summary>
    Partial,

    /// <summary>
    ///     Substitui o valor por um HMAC-SHA256 irreversível, porém correlacionável entre logs (mesmo valor →
    ///     mesmo hash). Requer uma chave em <c>Logging:Redaction:HashKey</c>; sem chave, recai em
    ///     <see cref="Mask" />.
    /// </summary>
    Hash
}
