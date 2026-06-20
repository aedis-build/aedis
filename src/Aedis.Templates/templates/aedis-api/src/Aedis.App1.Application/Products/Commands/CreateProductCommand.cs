using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Commands;

/// <summary>
///     Comando de criação de um produto. O resultado é o <see cref="Product" /> persistido (com identidade e
///     auditoria preenchidas).
/// </summary>
/// <param name="Code">Código de negócio (chave natural).</param>
/// <param name="Name">Nome do produto.</param>
/// <param name="Price">Preço do produto.</param>
public sealed record CreateProductCommand(string Code, string Name, decimal Price) : ICommand<Product>;
