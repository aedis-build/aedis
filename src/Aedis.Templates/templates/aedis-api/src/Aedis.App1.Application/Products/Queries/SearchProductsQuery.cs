using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;
using Aedis.Core;

namespace Aedis.App1.Application.Products.Queries;

/// <summary>
///     Consulta paginada de produtos com filtros opcionais por código e nome. O resultado é um
///     <see cref="PagedResult{T}" /> — itens + metadados de paginação em um só objeto.
/// </summary>
/// <param name="Code">Filtro exato por código, opcional.</param>
/// <param name="Name">Filtro parcial por nome, opcional.</param>
/// <param name="Page">Página solicitada (1-based).</param>
/// <param name="PageSize">Tamanho da página.</param>
public sealed record SearchProductsQuery(string? Code, string? Name, int Page = 1, int PageSize = 20) : ICommand<PagedResult<Product>>;
