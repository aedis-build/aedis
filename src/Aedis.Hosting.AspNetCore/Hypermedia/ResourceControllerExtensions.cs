using Aedis.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Extensões de controller que aplicam hipermídia de forma opcional e não intrusiva. Resolvem o provedor
///     de links do escopo da requisição; sem provedor registrado para o tipo, devolvem o objeto cru
///     (degradação graciosa), de modo que a hipermídia é ativada por tipo sem quebrar respostas existentes.
/// </summary>
public static class ResourceControllerExtensions {
    /// <summary>
    ///     Envolve um modelo único em <see cref="Resource{T}" /> com seus links, retornando <c>200 OK</c>;
    ///     <c>404 Not Found</c> quando o modelo é nulo, e o objeto cru quando não há provedor registrado.
    /// </summary>
    /// <typeparam name="T">Tipo do modelo de resposta.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="model">Modelo a expor, ou nulo para sinalizar recurso inexistente.</param>
    public static IActionResult AsResource<T>(this ControllerBase controller, T? model) {
        ArgumentNullException.ThrowIfNull(controller);

        if (model is null) {
            return controller.NotFound();
        }

        var links = controller.HttpContext.RequestServices.GetService<IResourceLinks<T>>();
        return controller.Ok(links is null ? model : links.Build(model, controller.HttpContext));
    }

    /// <summary>
    ///     Envolve uma página em <see cref="ResourceCollection{T}" />: cada item ganha seus próprios
    ///     <c>_links</c> (quando há provedor registrado) e a coleção recebe os links de paginação. Retorna
    ///     <c>200 OK</c>.
    /// </summary>
    /// <typeparam name="T">Tipo de cada item da coleção.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="page">Página de resultados (itens + metadados de paginação).</param>
    public static IActionResult AsCollection<T>(this ControllerBase controller, PagedResult<T> page) {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(page);

        var httpContext = controller.HttpContext;
        var links = httpContext.RequestServices.GetService<IResourceLinks<T>>();

        var items = page.Items
            .Select(item => links is null ? new Resource<T>(item) : links.Build(item, httpContext))
            .ToList();

        var collection = new ResourceCollection<T>(items, page.Total, page.Page, page.PageSize);
        PaginationLinks.Apply(collection, httpContext, page.Page, page.PageSize, page.Total);

        return controller.Ok(collection);
    }
}
