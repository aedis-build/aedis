using Aedis.Commands.Abstractions;
using Aedis.Hosting.AspNetCore.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Controllers;

/// <summary>
///     Base para controllers REST do Aedis. Mantém o controller fino: a regra de negócio vive nos handlers CQRS
///     (executados via <see cref="ICommandExecutor" />) e a representação de hipermídia é montada pelos helpers
///     HATEOAS. Padroniza os códigos de status RESTful — <c>201 Created</c> com header <c>Location</c> na
///     criação, <c>200 OK</c> com links na leitura/atualização e <c>204 No Content</c> na remoção — deixando o
///     mapeamento de exceções de negócio para o middleware global de ProblemDetails.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase {
    /// <summary>
    ///     Cria o controller com o executor de comandos CQRS.
    /// </summary>
    /// <param name="commandExecutor">Executor que resolve e roda o handler de cada comando/consulta.</param>
    /// <exception cref="ArgumentNullException">Quando <paramref name="commandExecutor" /> é nulo.</exception>
    protected ApiControllerBase(ICommandExecutor commandExecutor) {
        CommandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
    }

    /// <summary>
    ///     Executor de comandos/consultas. Use <see cref="ICommandExecutor.ExecuteAsync{TResult}" /> nos actions
    ///     para delegar a regra de negócio aos handlers.
    /// </summary>
    protected ICommandExecutor CommandExecutor { get; }

    /// <summary>
    ///     Retorna <c>200 OK</c> com o modelo envolvido em <see cref="Resource{T}" /> e seus links, ou
    ///     <c>404 Not Found</c> quando o modelo é nulo. Ideal para <c>GET</c> de item único.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta.</typeparam>
    /// <param name="model">Modelo a expor, ou nulo quando o recurso não existe.</param>
    protected IActionResult OkWithHateoas<TResponse>(TResponse? model) {
        return this.Hateoas(model);
    }

    /// <summary>
    ///     Retorna <c>200 OK</c> com a coleção envolvida em <see cref="CollectionResource{T}" /> e os links de
    ///     paginação. Ideal para <c>GET</c> de listagem.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de cada item da coleção.</typeparam>
    /// <param name="items">Itens da página atual.</param>
    /// <param name="page">Número da página atual (1-based), quando aplicável.</param>
    /// <param name="pageSize">Quantidade de itens por página, quando aplicável.</param>
    /// <param name="totalCount">Total de itens em todas as páginas, quando conhecido.</param>
    protected IActionResult CollectionWithHateoas<TResponse>(IEnumerable<TResponse> items, int? page = null, int? pageSize = null, int? totalCount = null) {
        return this.HateoasCollection(items, page, pageSize, totalCount);
    }

    /// <summary>
    ///     Retorna <c>201 Created</c> com o header <c>Location</c> apontando para a ação informada e o corpo
    ///     envolvido em <see cref="Resource{T}" /> com seus links. Ideal para a resposta de <c>POST</c>.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta criado.</typeparam>
    /// <param name="actionName">Nome do action que expõe o recurso criado (para o header <c>Location</c>).</param>
    /// <param name="routeValues">Valores de rota que identificam o recurso criado.</param>
    /// <param name="model">Modelo do recurso criado.</param>
    protected IActionResult CreatedWithHateoas<TResponse>(string actionName, object? routeValues, TResponse model) {
        return CreatedAtAction(actionName, routeValues, BuildResourcePayload(model));
    }

    private object BuildResourcePayload<TResponse>(TResponse model) {
        var builder = HttpContext.RequestServices.GetService<IResourceLinkBuilder<TResponse>>();
        return builder is null ? model! : builder.Build(model, HttpContext);
    }
}
