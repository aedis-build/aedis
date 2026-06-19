using Aedis.Database.Abstractions;

namespace Aedis.Database.Postgres.Queries;

/// <summary>
///     Critério de SQL bruto, parametrizado e <em>seguro contra SQL injection por construção</em>.
///     <para>
///         A segurança vem da <strong>parametrização</strong>, não de "sanitização" de string: o objeto
///         <c>parameters</c> é entregue ao Dapper, que envia cada valor como
///         <em>bind parameter</em> ao PostgreSQL — os valores nunca são concatenados no texto do SQL, de
///         modo que payloads de injeção em valores são tratados como dados literais, sem efeito.
///     </para>
///     <para>
///         Regras de uso seguro:
///         <list type="bullet">
///             <item>todo <strong>valor</strong> deve entrar como parâmetro (<c>@nome</c>) — nunca
///             interpole entrada do usuário no texto do SQL;</item>
///             <item><strong>identificadores</strong> dinâmicos (tabela/coluna/ordenação) não podem ser
///             parametrizados — valide-os com <see cref="SqlIdentifier.Validate" /> antes de compô-los.</item>
///         </list>
///     </para>
///     Funciona em qualquer ponto que receba <see cref="ICriteria{TEntity}" /> (Find/Count/Query/Command).
/// </summary>
/// <example>
///     <code>
///     var criteria = new RawCriteria&lt;Order&gt;(
///         "SELECT * FROM orders WHERE customer_id = @customerId AND total &gt;= @min",
///         new { customerId, min = 100m });
///     var orders = await repository.FindAsync(criteria);
///     </code>
/// </example>
public sealed class RawCriteria<TEntity> : ICriteria<TEntity>
{
    private readonly object _parameters;
    private readonly string _sql;

    /// <summary>
    ///     Cria o critério a partir do texto SQL e do objeto de parâmetros (anônimo ou dicionário) entregue
    ///     ao Dapper como bind parameters. Quando <paramref name="parameters" /> é nulo, usa um objeto vazio.
    /// </summary>
    /// <param name="sql">SQL bruto a executar; valores devem entrar como parâmetros (<c>@nome</c>).</param>
    /// <param name="parameters">Valores nomeados ligados ao SQL; nunca concatenados no texto.</param>
    public RawCriteria(string sql, object? parameters = null) {
        _sql = sql ?? throw new ArgumentNullException(nameof(sql));
        _parameters = parameters ?? new { };
    }

    /// <inheritdoc />
    public bool IsDistinct { get; init; }

    /// <inheritdoc />
    public (string Sql, object Parameters) Build() => (_sql, _parameters);
}
