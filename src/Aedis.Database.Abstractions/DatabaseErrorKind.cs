namespace Aedis.Database.Abstractions;

/// <summary>
///     Classe normalizada de um erro do banco, independente do provider (PostgreSQL, SQL Server). Permite que a
///     aplicação reaja a violações de integridade e a falhas transitórias sem conhecer <c>SqlState</c> ou
///     <c>Number</c> específicos.
/// </summary>
public enum DatabaseErrorKind
{
    /// <summary>Violação de chave única ou índice único.</summary>
    UniqueViolation,

    /// <summary>Violação de chave estrangeira.</summary>
    ForeignKeyViolation,

    /// <summary>Violação de restrição <c>CHECK</c>.</summary>
    CheckViolation,

    /// <summary>Violação de restrição de exclusão (ex.: sobreposição de intervalos).</summary>
    ExclusionViolation,

    /// <summary>Valor nulo em coluna <c>NOT NULL</c>.</summary>
    NotNullViolation,

    /// <summary>Falha de serialização entre transações concorrentes; repetir costuma resolver.</summary>
    SerializationFailure,

    /// <summary>Deadlock detectado pelo banco; a transação foi escolhida como vítima.</summary>
    Deadlock,

    /// <summary>Tempo limite do comando ou cancelamento pelo servidor.</summary>
    Timeout,

    /// <summary>Falha de conexão ou de rede com o banco.</summary>
    ConnectionFailure,

    /// <summary>Erro reconhecido como do banco, mas sem classe específica.</summary>
    Other
}
