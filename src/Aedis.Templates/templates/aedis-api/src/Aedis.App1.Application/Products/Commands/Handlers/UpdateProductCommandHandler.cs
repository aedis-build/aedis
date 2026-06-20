using Aedis.App1.Application.Abstractions;
using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;
using Aedis.Exceptions;

namespace Aedis.App1.Application.Products.Commands.Handlers;

/// <summary>
///     Handler de atualização. Carrega o agregado (ausência vira <c>404</c>), delega a transição de estado à
///     entidade e persiste.
/// </summary>
public sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand, Product> {
    private readonly IProductRepository _repository;

    /// <summary>
    ///     Cria o handler com o repositório de produtos.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    public UpdateProductCommandHandler(IProductRepository repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Product> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken = default) {
        var product = await _repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new BusinessException("Produto não encontrado.", ViolationType.BusinessError, rule: "id", statusCode: 404);

        product.Update(command.Name, command.Price);
        return await _repository.SaveAsync(product, cancellationToken);
    }
}
