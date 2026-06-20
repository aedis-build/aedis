using Aedis.App1.Application.Abstractions;
using Aedis.Commands.Abstractions;
using Aedis.Exceptions;

namespace Aedis.App1.Application.Products.Commands.Handlers;

/// <summary>
///     Handler de remoção. Carrega o agregado (ausência vira <c>404</c>) e o remove via repositório
///     (soft-delete quando a entidade possui coluna <c>IsDeleted</c>).
/// </summary>
public sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand, DeleteProductResult> {
    private readonly IProductRepository _repository;

    /// <summary>
    ///     Cria o handler com o repositório de produtos.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    public DeleteProductCommandHandler(IProductRepository repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<DeleteProductResult> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken = default) {
        var product = await _repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new BusinessException("Produto não encontrado.", ViolationType.BusinessError, rule: "id", statusCode: 404);

        await _repository.DeleteAsync(product.Id, cancellationToken);
        return new DeleteProductResult(true);
    }
}
