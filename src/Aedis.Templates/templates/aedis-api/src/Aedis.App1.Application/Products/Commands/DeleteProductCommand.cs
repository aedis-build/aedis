using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Commands;

/// <summary>
///     Comando de remoção de um produto pela identidade.
/// </summary>
/// <param name="Id">Identidade do produto a remover.</param>
public sealed record DeleteProductCommand(Guid Id) : ICommand<DeleteProductResult>;

/// <summary>
///     Resultado da remoção de um produto.
/// </summary>
/// <param name="Deleted">Indica se a remoção foi efetivada.</param>
public sealed record DeleteProductResult(bool Deleted);
