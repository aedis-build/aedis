using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Hateoas;

/// <summary>
///     Extensões de controller que aplicam HATEOAS de forma opcional e não intrusiva. Resolvem o builder de
///     links do escopo da requisição; se não houver builder registrado para o tipo, devolvem o objeto cru
///     (degradação graciosa), de modo que o HATEOAS é ativado por tipo sem quebrar respostas existentes.
/// </summary>
public static class HateoasControllerExtensions {
    /// <summary>
    ///     Envolve um modelo único em <see cref="Resource{T}" /> com seus links, retornando <c>200 OK</c>.
    ///     Retorna <c>404 Not Found</c> quando o modelo é nulo e o objeto cru quando não há builder registrado.
    /// </summary>
    /// <typeparam name="T">Tipo do modelo de resposta.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="model">Modelo a expor, ou nulo para sinalizar recurso inexistente.</param>
    public static IActionResult Hateoas<T>(this ControllerBase controller, T? model) {
        ArgumentNullException.ThrowIfNull(controller);

        if (model is null) {
            return controller.NotFound();
        }

        var builder = controller.HttpContext.RequestServices.GetService<IResourceLinkBuilder<T>>();
        return builder is null
            ? controller.Ok(model)
            : controller.Ok(builder.Build(model, controller.HttpContext));
    }

    /// <summary>
    ///     Envolve uma coleção em <see cref="CollectionResource{T}" /> com os links de paginação, retornando
    ///     <c>200 OK</c>. Devolve a lista crua quando não há builder de coleção registrado para o tipo.
    /// </summary>
    /// <typeparam name="T">Tipo de cada item da coleção.</typeparam>
    /// <param name="controller">Controller em execução.</param>
    /// <param name="items">Itens da página atual.</param>
    /// <param name="page">Número da página atual (1-based), quando aplicável.</param>
    /// <param name="pageSize">Quantidade de itens por página, quando aplicável.</param>
    /// <param name="totalCount">Total de itens em todas as páginas, quando conhecido.</param>
    public static IActionResult HateoasCollection<T>(this ControllerBase controller, IEnumerable<T> items, int? page = null, int? pageSize = null, int? totalCount = null) {
        ArgumentNullException.ThrowIfNull(controller);

        var list = items as IReadOnlyList<T> ?? items?.ToList() ?? [];
        var builder = controller.HttpContext.RequestServices.GetService<ICollectionLinkBuilder<T>>();
        return builder is null
            ? controller.Ok(list)
            : controller.Ok(builder.Build(list, controller.HttpContext, page, pageSize, totalCount));
    }
}
