namespace Aedis.Database.Abstractions;

public interface ICriteria<TEntity>
{
    (string Sql, object Parameters) Build();

    /// <summary>
    ///     Indica se a query usa SELECT DISTINCT. Quando true, o repositório envolve a
    ///     contagem em subquery para preservar a semântica do DISTINCT.
    /// </summary>
    bool IsDistinct => false;
}