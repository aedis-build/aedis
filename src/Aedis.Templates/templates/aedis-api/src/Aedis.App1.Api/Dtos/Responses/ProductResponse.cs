namespace Aedis.App1.Api.Dtos.Responses;

/// <summary>
///     Representação de saída de um produto. É este o tipo envolvido pelo HATEOAS sob <c>data</c>, com os
///     links (<c>self</c>/<c>update</c>/<c>delete</c>/<c>collection</c>) sob <c>_links</c>.
/// </summary>
/// <param name="Id">Identidade do produto.</param>
/// <param name="Code">Código de negócio.</param>
/// <param name="Name">Nome do produto.</param>
/// <param name="Price">Preço do produto.</param>
/// <param name="CreatedAt">Momento de criação.</param>
/// <param name="UpdatedAt">Momento da última atualização, quando houve.</param>
public sealed record ProductResponse(Guid Id, string Code, string Name, decimal Price, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
