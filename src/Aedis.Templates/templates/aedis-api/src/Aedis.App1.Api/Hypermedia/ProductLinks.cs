using Aedis.App1.Api.Dtos.Responses;
using Aedis.Hosting.AspNetCore.Hypermedia;

namespace Aedis.App1.Api.Hypermedia;

/// <summary>
///     Links de hipermídia de um produto. Os destinos são resolvidos por <em>action</em> do
///     <c>ProductsController</c> (sem URL na mão e refator-safe). Para exibir ações condicionadas a permissão,
///     injete <c>ICurrentUser</c> e só declare o link quando o papel exigido estiver presente — assim a
///     resposta reflete o que o cliente realmente pode fazer.
/// </summary>
public sealed class ProductLinks : ResourceLinks<ProductResponse> {
    /// <inheritdoc />
    protected override void Configure(ILinkMap links, ProductResponse product) {
        links.Self("GetById", new { id = product.Id });
        links.Action("update", "PUT", "Update", new { id = product.Id });
        links.Action("delete", "DELETE", "Delete", new { id = product.Id });
        links.Collection("Search");
    }
}
