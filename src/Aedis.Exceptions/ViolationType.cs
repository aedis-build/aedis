namespace Aedis.Exceptions;

/// <summary>
///     Tipo de violação que representa a categoria do erro de negócio.
///     Usado pela <see cref="BusinessException" /> para determinar a categoria do erro.
/// </summary>
public enum ViolationType
{
    /// <summary>
    ///     Erro de validação (validações de modelo, regras de negócio).
    ///     Categoria: <c>validation</c>
    ///     Status padrão: 422 (Unprocessable Entity)
    /// </summary>
    ValidationError,

    /// <summary>
    ///     Violação de chave estrangeira do banco de dados.
    ///     Categoria: <c>business</c>
    ///     Status padrão: 422 (Unprocessable Entity)
    /// </summary>
    ForeignKeyViolation,

    /// <summary>
    ///     Violação de constraint único do banco de dados.
    ///     Categoria: <c>business</c>
    ///     Status padrão: 422 (Unprocessable Entity)
    /// </summary>
    UniqueConstraintViolation,

    /// <summary>
    ///     Conflito de estado (recurso duplicado, estado conflitante).
    ///     Categoria: <c>business</c>
    ///     Status padrão: 409 (Conflict)
    /// </summary>
    ConflictError,

    /// <summary>
    ///     Pré-condição falhou (ETag mismatch, status incorreto).
    ///     Categoria: <c>business</c>
    ///     Status padrão: 412 (Precondition Failed)
    /// </summary>
    PreconditionFailed,

    /// <summary>
    ///     Erro de negócio genérico (regras de negócio não atendidas).
    ///     Categoria: <c>business</c>
    ///     Status padrão: 422 (Unprocessable Entity)
    /// </summary>
    BusinessError
}