namespace Aedis.App1.Api.Dtos.Requests;

/// <summary>
///     Corpo da requisição de criação de produto.
/// </summary>
/// <param name="Code">Código de negócio (chave natural).</param>
/// <param name="Name">Nome do produto.</param>
/// <param name="Price">Preço do produto.</param>
public sealed record CreateProductRequest(string Code, string Name, decimal Price);
