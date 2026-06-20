using Aedis.App1.Api.Dtos.Responses;
using Aedis.Hosting.AspNetCore.Hateoas;
using Microsoft.AspNetCore.Http;

namespace Aedis.App1.Api.Hateoas;

/// <summary>
///     Constrói os links de hipermídia de um produto. Anexa <c>self</c> e as ações disponíveis
///     (<c>update</c>/<c>delete</c>) com seus verbos, além do link para a <c>collection</c>. Para exibir ações
///     condicionadas a permissão, injete <c>ICurrentUser</c> e só adicione o link quando o papel exigido estiver
///     presente — assim o HATEOAS reflete o que o cliente realmente pode fazer.
/// </summary>
public sealed class ProductLinkBuilder : IResourceLinkBuilder<ProductResponse> {
    /// <inheritdoc />
    public Resource<ProductResponse> Build(ProductResponse model, HttpContext httpContext) {
        var resource = new Resource<ProductResponse>(model);
        var baseUrl = httpContext.GetBaseUrl();
        var self = $"{baseUrl}/v1/products/{model.Id}";

        resource.AddLink("self", self);
        resource.AddLink("update", self, "PUT");
        resource.AddLink("delete", self, "DELETE");
        resource.AddLink("collection", $"{baseUrl}/v1/products");

        return resource;
    }
}
