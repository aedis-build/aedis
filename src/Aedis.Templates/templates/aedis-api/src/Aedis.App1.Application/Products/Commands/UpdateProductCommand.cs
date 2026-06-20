using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Commands;

/// <summary>
///     Comando de atualização de um produto existente. O resultado é o <see cref="Product" /> já atualizado.
/// </summary>
/// <param name="Id">Identidade do produto a atualizar.</param>
/// <param name="Name">Novo nome.</param>
/// <param name="Price">Novo preço.</param>
public sealed record UpdateProductCommand(Guid Id, string Name, decimal Price) : ICommand<Product>;
