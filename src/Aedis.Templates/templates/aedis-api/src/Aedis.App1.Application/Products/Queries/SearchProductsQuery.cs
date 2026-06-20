using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Queries;

/// <summary>
///     Consulta paginada de produtos com filtros opcionais por código e nome.
/// </summary>
/// <param name="Code">Filtro exato por código, opcional.</param>
/// <param name="Name">Filtro parcial por nome, opcional.</param>
/// <param name="Page">Página solicitada (1-based).</param>
/// <param name="PageSize">Tamanho da página.</param>
public sealed record SearchProductsQuery(string? Code, string? Name, int Page = 1, int PageSize = 20) : ICommand<SearchProductsResult>;

/// <summary>
///     Resultado da consulta paginada: os itens da página e os metadados de paginação.
/// </summary>
/// <param name="Items">Produtos da página atual.</param>
/// <param name="Total">Total de produtos que satisfazem o filtro.</param>
/// <param name="Page">Página atual (1-based).</param>
/// <param name="PageSize">Tamanho da página.</param>
public sealed record SearchProductsResult(IReadOnlyList<Product> Items, int Total, int Page, int PageSize);
