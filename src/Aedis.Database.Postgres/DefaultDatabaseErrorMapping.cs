using Aedis.Database.Abstractions;
using Aedis.Exceptions;

namespace Aedis.Database.Postgres;

/// <summary>
///     Mapeamento padrão do framework quando nem o hook do repositório nem o <see cref="IDatabaseErrorMapper" />
///     resolvem o erro: violações de integridade viram <see cref="BusinessException" /> com a regra igual ao
///     nome da restrição; falhas transitórias (serialização, deadlock, timeout, conexão) viram
///     <see cref="ServiceTemporarilyUnavailableException" /> para o chamador poder repetir. As mensagens são
///     neutras — a mensagem crua do servidor fica apenas na exceção original encadeada.
/// </summary>
internal static class DefaultDatabaseErrorMapping
{
    internal static Exception? Map(DatabaseError error) {
        return error.Kind switch {
            DatabaseErrorKind.UniqueViolation => Business("Registro duplicado: violação de unicidade", error, ViolationType.UniqueConstraintViolation),
            DatabaseErrorKind.ForeignKeyViolation => Business("Referência inválida: violação de chave estrangeira", error, ViolationType.ForeignKeyViolation),
            DatabaseErrorKind.ExclusionViolation => Business("Conflito com registro existente: violação de exclusão", error, ViolationType.ConflictError),
            DatabaseErrorKind.CheckViolation => Business("Valor inválido: violação de restrição de verificação", error, ViolationType.ValidationError),
            DatabaseErrorKind.NotNullViolation => Business("Valor obrigatório ausente", error, ViolationType.ValidationError),
            DatabaseErrorKind.SerializationFailure or DatabaseErrorKind.Deadlock or DatabaseErrorKind.Timeout or DatabaseErrorKind.ConnectionFailure
                => new ServiceTemporarilyUnavailableException(error.Provider, DescribeTransient(error), error.OriginalException),
            _ => null
        };
    }

    private static BusinessException Business(string message, DatabaseError error, ViolationType violationType) {
        var detail = error.ConstraintName is null ? message : $"{message} ({error.ConstraintName})";
        return new BusinessException(detail + ".", violationType, error.ConstraintName, error.OriginalException);
    }

    private static string DescribeTransient(DatabaseError error) {
        return error.Kind switch {
            DatabaseErrorKind.Deadlock => "Deadlock detectado pelo banco; repita a operação.",
            DatabaseErrorKind.SerializationFailure => "Falha de serialização entre transações concorrentes; repita a operação.",
            DatabaseErrorKind.Timeout => "Tempo limite excedido no banco.",
            _ => "Falha de conexão com o banco."
        };
    }
}
