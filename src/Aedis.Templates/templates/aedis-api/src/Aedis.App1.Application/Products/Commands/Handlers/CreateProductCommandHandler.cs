using Aedis.App1.Application.Abstractions;
using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;
using Aedis.Exceptions;

namespace Aedis.App1.Application.Products.Commands.Handlers;

/// <summary>
///     Handler de criação. Garante a unicidade da chave natural (código) antes de persistir; o conflito vira
///     <c>409</c> via <see cref="BusinessException" />. A auditoria é carimbada pelo repositório.
/// </summary>
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Product> {
    private readonly IProductRepository _repository;

    /// <summary>
    ///     Cria o handler com o repositório de produtos.
    /// </summary>
    /// <param name="repository">Porta de saída do agregado.</param>
    public CreateProductCommandHandler(IProductRepository repository) {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<Product> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default) {
        var existing = await _repository.GetByCodeAsync(command.Code, cancellationToken);
        if (existing is not null) {
            throw new BusinessException($"Já existe um produto com o código '{command.Code}'.", ViolationType.ConflictError, rule: "code");
        }

        var product = Product.Create(command.Code, command.Name, command.Price);
        return await _repository.SaveAsync(product, cancellationToken);
    }
}
