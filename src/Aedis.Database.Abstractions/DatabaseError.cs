namespace Aedis.Database.Abstractions;

/// <summary>
///     Erro do banco normalizado pelo provider: traz a classe (<see cref="Kind" />), os identificadores nativos
///     e, quando o servidor informa, o nome da restrição, da coluna e da tabela envolvidas. É a entrada do hook
///     <c>OnDatabaseError</c> do repositório e do <see cref="IDatabaseErrorMapper" />.
/// </summary>
/// <param name="Kind">Classe normalizada do erro.</param>
/// <param name="Provider">Identificador do provider que traduziu o erro (ex.: <c>postgres</c>, <c>sqlserver</c>).</param>
/// <param name="SqlState">Código <c>SQLSTATE</c> nativo, quando o provider o expõe.</param>
/// <param name="Number">Número de erro nativo, quando o provider o expõe.</param>
/// <param name="ConstraintName">Nome da restrição violada, quando informado pelo servidor.</param>
/// <param name="ColumnName">Nome da coluna envolvida, quando informado pelo servidor.</param>
/// <param name="TableName">Nome da tabela envolvida, quando informado pelo servidor.</param>
/// <param name="Message">Mensagem original do servidor — útil em logs; evite devolvê-la ao cliente.</param>
/// <param name="OriginalException">Exceção original do driver, preservada como causa.</param>
public sealed record DatabaseError(
    DatabaseErrorKind Kind,
    string Provider,
    string? SqlState,
    int? Number,
    string? ConstraintName,
    string? ColumnName,
    string? TableName,
    string Message,
    Exception OriginalException);
