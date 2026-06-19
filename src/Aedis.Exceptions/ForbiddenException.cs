namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando o usuário está autenticado mas não possui permissão
///     para acessar um recurso específico (HTTP 403 Forbidden).
///     Diferente de UnauthorizedAccessException (401), que indica falta de autenticação.
/// </summary>
public class ForbiddenException : Exception
{
    /// <summary>Cria a exceção indicando, opcionalmente, o recurso acessado e a permissão exigida.</summary>
    public ForbiddenException(string message, string? resource = null, string? requiredPermission = null)
        : base(message) {
        Resource = resource;
        RequiredPermission = requiredPermission;
    }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />).</summary>
    public ForbiddenException(string message, Exception innerException, string? resource = null,
        string? requiredPermission = null)
        : base(message, innerException) {
        Resource = resource;
        RequiredPermission = requiredPermission;
    }

    /// <summary>
    ///     Recurso que o usuário tentou acessar (ex: "/api/orders/123").
    /// </summary>
    public string? Resource { get; }

    /// <summary>
    ///     Permissão requerida que o usuário não possui (ex: "orders:read", "users:write").
    /// </summary>
    public string? RequiredPermission { get; }
}