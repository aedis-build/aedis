namespace Aedis.App1.Api.Dtos.Requests;

/// <summary>
///     Corpo da requisição de atualização de produto (a chave natural <c>Code</c> é imutável).
/// </summary>
/// <param name="Name">Novo nome.</param>
/// <param name="Price">Novo preço.</param>
public sealed record UpdateProductRequest(string Name, decimal Price);
