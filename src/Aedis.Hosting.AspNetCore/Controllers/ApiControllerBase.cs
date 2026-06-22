using Aedis.Commands.Abstractions;
using Aedis.Core;
using Aedis.Hosting.AspNetCore.Hypermedia;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Controllers;

/// <summary>
///     Base para controllers REST do Aedis. Mantém o controller fino: a regra de negócio vive nos handlers CQRS
///     (executados via <see cref="ICommandExecutor" />) e a representação de hipermídia é montada pelos helpers
///     <c>OkResource</c>/<c>OkCollection</c>/<c>CreatedResource</c>. Padroniza os códigos RESTful — <c>201</c>
///     com <c>Location</c> na criação, <c>200</c> com links na leitura/atualização e <c>204</c> na remoção —
///     deixando o mapeamento de exceções de negócio para o middleware global de ProblemDetails.
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
    protected IActionResult OkResource<TResponse>(TResponse? model) {
        return this.AsResource(model);
    }

    /// <summary>
    ///     Retorna <c>200 OK</c> com a página envolvida em <see cref="ResourceCollection{T}" />: cada item com
    ///     seus links e a coleção com os links de paginação. Ideal para <c>GET</c> de listagem.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de cada item da coleção.</typeparam>
    /// <param name="page">Página de resultados (itens + metadados de paginação).</param>
    protected IActionResult OkCollection<TResponse>(PagedResult<TResponse> page) {
        return this.AsCollection(page);
    }

    /// <summary>
    ///     Retorna <c>201 Created</c> com o header <c>Location</c> apontando para a ação informada e o corpo
    ///     envolvido em <see cref="Resource{T}" /> com seus links. Ideal para a resposta de <c>POST</c>.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta criado.</typeparam>
    /// <param name="model">Modelo do recurso criado.</param>
    /// <param name="actionName">Nome do action que expõe o recurso criado (para o header <c>Location</c>).</param>
    /// <param name="routeValues">Valores de rota que identificam o recurso criado.</param>
    protected IActionResult CreatedResource<TResponse>(TResponse model, string actionName, object? routeValues) {
        return CreatedAtAction(actionName, routeValues, BuildResourcePayload(model));
    }

    private object BuildResourcePayload<TResponse>(TResponse model) {
        var links = HttpContext.RequestServices.GetService<IResourceLinks<TResponse>>();
        return links is null ? model! : links.Build(model, HttpContext);
    }
}
