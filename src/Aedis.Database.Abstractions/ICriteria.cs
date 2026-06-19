namespace Aedis.Database.Abstractions;

/// <summary>
///     Critério de consulta de uma entidade: encapsula a montagem de um SQL parametrizado a ser executado
///     pelo repositório (Find/Count/Query/Command). As implementações devem sempre enviar valores como bind
///     parameters; veja <c>PostgresCriteria</c> (builder fluente) e <c>RawCriteria</c> (SQL bruto).
/// </summary>
public interface ICriteria<TEntity>
{
    /// <summary>Monta o SQL e o objeto de parâmetros a serem passados ao executor.</summary>
    (string Sql, object Parameters) Build();

    /// <summary>
    ///     Indica se a query usa SELECT DISTINCT. Quando true, o repositório envolve a
    ///     contagem em subquery para preservar a semântica do DISTINCT.
    /// </summary>
    bool IsDistinct => false;
}