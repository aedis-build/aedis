using Aedis.App1.Application.Abstractions;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Queries.Handlers;

/// <summary>
///     Handler da consulta paginada. Normaliza os parâmetros de paginação (página mínima 1, tamanho entre 1 e
///     100) antes de delegar a busca ao repositório.
/// </summary>
public sealed class SearchProductsQueryHandler : ICommandHandler<SearchProductsQuery, SearchProductsResult> {
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IProductRepository _repository;

    /// <summary>
    ///     Cria o handler com o repositório de produtos.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    public SearchProductsQueryHandler(IProductRepository repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<SearchProductsResult> HandleAsync(SearchProductsQuery query, CancellationToken cancellationToken = default) {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;

        var (items, total) = await _repository.SearchAsync(query.Code, query.Name, page, pageSize, cancellationToken);
        return new SearchProductsResult(items, total, page, pageSize);
    }
}
