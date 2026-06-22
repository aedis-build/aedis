namespace Aedis.Core;

/// <summary>
///     Página de um resultado paginado: os itens da página atual mais os metadados de paginação
///     (<see cref="Total" />, <see cref="Page" />, <see cref="PageSize" />). Use como retorno de consultas e
///     repositórios para trafegar a paginação como um único objeto, em vez de tuplas ou parâmetros soltos.
/// </summary>
/// <typeparam name="T">Tipo de cada item da página.</typeparam>
/// <param name="Items">Itens da página atual.</param>
/// <param name="Total">Total de itens em todas as páginas.</param>
/// <param name="Page">Número da página atual (1-based).</param>
/// <param name="PageSize">Quantidade de itens por página.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize) {
    /// <summary>
    ///     Projeta cada item para outro tipo, preservando os metadados de paginação. Útil para mapear de
    ///     entidades de domínio para DTOs de resposta sem perder a paginação.
    /// </summary>
    /// <typeparam name="TOut">Tipo de saída de cada item.</typeparam>
    /// <param name="selector">Função de projeção item a item.</param>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) {
        return new PagedResult<TOut>(Items.Select(selector).ToList(), Total, Page, PageSize);
    }

    /// <summary>
    ///     Cria uma página vazia para os parâmetros de paginação informados.
    /// </summary>
    /// <param name="page">Número da página (1-based).</param>
    /// <param name="pageSize">Quantidade de itens por página.</param>
    public static PagedResult<T> Empty(int page, int pageSize) {
        return new PagedResult<T>([], 0, page, pageSize);
    }
}
