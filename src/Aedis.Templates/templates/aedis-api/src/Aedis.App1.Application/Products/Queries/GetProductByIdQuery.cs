using Aedis.App1.Domain.Entities;
using Aedis.Commands.Abstractions;

namespace Aedis.App1.Application.Products.Queries;

/// <summary>
///     Consulta de um produto pela identidade. Uma consulta é apenas um comando somente-leitura — reusa o mesmo
///     <see cref="ICommand{TResult}" />/executor do CQRS. Resultado <c>null</c> sinaliza recurso inexistente
///     (o controller traduz para <c>404</c>).
/// </summary>
/// <param name="Id">Identidade do produto.</param>
public sealed record GetProductByIdQuery(Guid Id) : ICommand<Product?>;
