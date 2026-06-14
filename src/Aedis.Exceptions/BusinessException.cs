namespace Aedis.Exceptions;

/// <summary>
///     Exceção de negócio que representa violações de regras de negócio.
///     Pode ser lançada em qualquer camada para indicar violações de regras de negócio.
/// </summary>
public class BusinessException : Exception
{
    /// <summary>
    ///     Construtor principal usando enum ViolationType (recomendado).
    /// </summary>
    public BusinessException(string message, ViolationType violationType, string? rule = null, int? statusCode = null)
        : base(message) {
        ViolationType = violationType;
        Rule = rule;
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Construtor com innerException usando enum ViolationType (recomendado).
    /// </summary>
    public BusinessException(string message, ViolationType violationType, string? rule, Exception innerException,
        int? statusCode = null)
        : base(message, innerException) {
        ViolationType = violationType;
        Rule = rule;
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Construtor legacy com string (para compatibilidade durante migração).
    ///     Será removido em versão futura. Use o construtor com enum ViolationType.
    /// </summary>
    [Obsolete("Use constructor with ViolationType enum instead. This constructor will be removed in a future version.")]
    public BusinessException(string message, string violationType, string? rule = null, int? statusCode = null)
        : base(message) {
        ViolationType = ViolationTypeExtensions.FromSnakeCase(violationType);
        Rule = rule;
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Construtor legacy com string e innerException (para compatibilidade durante migração).
    ///     Será removido em versão futura. Use o construtor com enum ViolationType.
    /// </summary>
    [Obsolete("Use constructor with ViolationType enum instead. This constructor will be removed in a future version.")]
    public BusinessException(string message, string violationType, string? rule, Exception innerException,
        int? statusCode = null)
        : base(message, innerException) {
        ViolationType = ViolationTypeExtensions.FromSnakeCase(violationType);
        Rule = rule;
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Regra que foi infringida. Pode ser o nome da propriedade/campo (ex: "partner_id")
    ///     ou um código de erro (ex: "PARTNER_NOT_FOUND").
    /// </summary>
    public string? Rule { get; }

    /// <summary>
    ///     Tipo de violação que representa a categoria do erro de negócio.
    ///     Usado para determinar a category do ProblemDetails.
    /// </summary>
    public ViolationType ViolationType { get; }

    /// <summary>
    ///     Status HTTP customizado. Se não especificado, usa o padrão baseado no ViolationType.
    ///     Permite sobrescrever o status code padrão quando necessário.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    ///     Categoria padrão baseada em ViolationType.
    ///     Mapeia violationType para category: validation, business, authentication, authorization, technical.
    /// </summary>
    public string Category => MapViolationTypeToCategory(ViolationType);

    /// <summary>
    ///     Status HTTP padrão baseado no ViolationType.
    /// </summary>
    public int DefaultStatusCode => MapViolationTypeToDefaultStatusCode(ViolationType);

    /// <summary>
    ///     Status HTTP efetivo (usa StatusCode se fornecido, caso contrário usa DefaultStatusCode).
    /// </summary>
    public int EffectiveStatusCode => StatusCode ?? DefaultStatusCode;

    /// <summary>
    ///     Mapeia ViolationType para category conforme padrão do Aedis.
    /// </summary>
    private static string MapViolationTypeToCategory(ViolationType violationType) {
        return violationType switch {
            ViolationType.ValidationError => "validation",
            ViolationType.ForeignKeyViolation => "business",
            ViolationType.UniqueConstraintViolation => "business",
            ViolationType.ConflictError => "business",
            ViolationType.PreconditionFailed => "business",
            ViolationType.BusinessError => "business",
            _ => "business"
        };
    }

    /// <summary>
    ///     Mapeia ViolationType para status code padrão.
    /// </summary>
    private static int MapViolationTypeToDefaultStatusCode(ViolationType violationType) {
        return violationType switch {
            ViolationType.ConflictError => 409,
            ViolationType.PreconditionFailed => 412,
            _ => 412 // Default para regras de negócio
        };
    }
}