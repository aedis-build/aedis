using Aedis.Exceptions;

namespace Aedis.Signing.Abstractions;

/// <summary>
///     Erro permanente de assinatura (chave inexistente, desabilitada, sem permissão, material inválido).
///     Herda de <see cref="PermanentFailureException" />: retentar não resolve. Erros transitórios do provider
///     viram <see cref="ServiceTemporarilyUnavailableException" />.
/// </summary>
public class SigningException : PermanentFailureException
{
    /// <summary>Cria a exceção com a mensagem e, opcionalmente, a chave envolvida.</summary>
    public SigningException(string message, string? keyId = null) : base(message) {
        KeyId = keyId;
        if (keyId is not null) AddErrorData("key_id", keyId);
    }

    /// <summary>Cria a exceção encadeando a causa original.</summary>
    public SigningException(string message, Exception innerException, string? keyId = null) : base(message, innerException) {
        KeyId = keyId;
        if (keyId is not null) AddErrorData("key_id", keyId);
    }

    /// <summary>Identificador, referência ou alias da chave envolvida, quando conhecido.</summary>
    public string? KeyId { get; }
}

/// <summary>A chave (ou o alias) não existe no provider e a criação automática está desligada.</summary>
public sealed class SigningKeyNotFoundException : SigningException
{
    /// <summary>Cria a exceção para a chave <paramref name="keyId" />.</summary>
    public SigningKeyNotFoundException(string keyId, string message) : base(message, keyId) { }

    /// <summary>Cria a exceção encadeando a causa original.</summary>
    public SigningKeyNotFoundException(string keyId, string message, Exception innerException)
        : base(message, innerException, keyId) { }
}

/// <summary>A chave existe mas não serve: desabilitada, agendada para exclusão, curva ou uso errados.</summary>
public sealed class SigningKeyInvalidStateException : SigningException
{
    /// <summary>Cria a exceção para a chave <paramref name="keyId" />.</summary>
    public SigningKeyInvalidStateException(string keyId, string message) : base(message, keyId) { }

    /// <summary>Cria a exceção encadeando a causa original.</summary>
    public SigningKeyInvalidStateException(string keyId, string message, Exception innerException)
        : base(message, innerException, keyId) { }
}

/// <summary>A identidade da aplicação não pode usar a chave (política de acesso do cofre).</summary>
public sealed class SigningAccessDeniedException : SigningException
{
    /// <summary>Cria a exceção para a chave <paramref name="keyId" /> encadeando a causa original.</summary>
    public SigningAccessDeniedException(string keyId, string message, Exception innerException)
        : base(message, innerException, keyId) { }
}
