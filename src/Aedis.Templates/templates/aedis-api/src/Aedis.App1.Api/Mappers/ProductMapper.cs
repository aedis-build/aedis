using Aedis.App1.Api.Dtos.Responses;
using Aedis.App1.Domain.Entities;

namespace Aedis.App1.Api.Mappers;

/// <summary>
///     Converte a entidade de domínio <see cref="Product" /> em <see cref="ProductResponse" />. Mapeamento
///     manual e explícito (sem AutoMapper), mantendo o contrato de saída sob controle da camada de API.
/// </summary>
public sealed class ProductMapper {
    /// <summary>
    ///     Projeta um produto para sua representação de saída. <c>UpdatedAt</c> vazio (nunca atualizado) vira
    ///     <c>null</c> na resposta.
    /// </summary>
    /// <param name="product">Entidade de domínio.</param>
    public ProductResponse ToResponse(Product product) {
        return new ProductResponse(
            product.Id,
            product.Code,
            product.Name,
            product.Price,
            product.CreatedAt,
            product.UpdatedAt == default ? null : product.UpdatedAt);
    }
}
