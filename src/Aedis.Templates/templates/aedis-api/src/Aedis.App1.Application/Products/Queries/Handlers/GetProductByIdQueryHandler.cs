using Aedis.App1.Application.Abstractions;
using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Queries.Handlers;

/// <summary>
///     Handler da consulta por identidade. Retorna o produto ou <c>null</c> quando não existe.
/// </summary>
public sealed class GetProductByIdQueryHandler : ICommandHandler<GetProductByIdQuery, Product?> {
    private readonly IProductRepository _repository;

    /// <summary>
    ///     Cria o handler com o repositório de produtos.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    public GetProductByIdQueryHandler(IProductRepository repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Product?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default) {
        return await _repository.GetByIdAsync(query.Id, cancellationToken);
    }
}
