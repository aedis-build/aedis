using Aedis.Commands.Abstractions;
using Aedis.Core;
using Aedis.Hypermedia.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Hosting.AspNetCore.Controllers;

/// <summary>
///     Base para controllers REST do Aedis. Mantém o controller fino: a regra de negócio vive nos handlers CQRS
///     (executados via <see cref="ICommandExecutor" />) e a representação é montada pelos helpers
///     <c>OkResource</c>/<c>OkCollection</c>/<c>CreatedResource</c>, que delegam ao plugin de hipermídia
///     (<see cref="IHypermedia" />, pacote <c>Aedis.Hypermedia.AspNetCore</c>) quando ele está registrado e
///     devolvem o modelo cru quando não está. Padroniza os códigos RESTful — <c>201</c> com <c>Location</c> na
///     criação, <c>200</c> na leitura/atualização e <c>204</c> na remoção — deixando o mapeamento de exceções
///     de negócio para o middleware global de ProblemDetails.
/// </summary>
[ApiController]
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
    ///     Retorna <c>200 OK</c> com o modelo envolvido em <see cref="Resource{T}" /> e seus links (ou o modelo
    ///     cru sem o plugin de hipermídia), ou <c>404 Not Found</c> quando o modelo é nulo. Ideal para
    ///     <c>GET</c> de item único.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta.</typeparam>
    /// <param name="model">Modelo a expor, ou nulo quando o recurso não existe.</param>
    protected IActionResult OkResource<TResponse>(TResponse? model) {
        if (model is null)
            return NotFound();

        return Ok(Hypermedia?.Resource(model) ?? (object)model);
    }

    /// <summary>
    ///     Retorna <c>200 OK</c> com a página envolvida em <see cref="ResourceCollection{T}" /> — cada item com
    ///     seus links e a coleção com os links de paginação — ou a <see cref="PagedResult{T}" /> crua sem o
    ///     plugin de hipermídia. Ideal para <c>GET</c> de listagem.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de cada item da coleção.</typeparam>
    /// <param name="page">Página de resultados (itens + metadados de paginação).</param>
    /// <param name="export">Links de exportação a anunciar; <c>null</c> para nenhum.</param>
    /// <param name="requestEcho">Request de consulta a ecoar nos <c>_templates</c>; <c>null</c> usa o capturado pelo filtro.</param>
    protected IActionResult OkCollection<TResponse>(PagedResult<TResponse> page, ExportLinkOptions? export = null, object? requestEcho = null) {
        ArgumentNullException.ThrowIfNull(page);

        return Ok(Hypermedia?.Collection(page, export, requestEcho) ?? (object)page);
    }

    /// <summary>
    ///     Retorna <c>201 Created</c> com o header <c>Location</c> apontando para a ação informada e o corpo
    ///     envolvido em <see cref="Resource{T}" /> com seus links (ou o modelo cru sem o plugin de hipermídia).
    ///     Ideal para a resposta de <c>POST</c>.
    /// </summary>
    /// <typeparam name="TResponse">Tipo do modelo de resposta criado.</typeparam>
    /// <param name="model">Modelo do recurso criado.</param>
    /// <param name="actionName">Nome do action que expõe o recurso criado (para o header <c>Location</c>).</param>
    /// <param name="routeValues">Valores de rota que identificam o recurso criado.</param>
    protected IActionResult CreatedResource<TResponse>(TResponse model, string actionName, object? routeValues) {
        return CreatedAtAction(actionName, routeValues, Hypermedia?.Resource(model) ?? (object?)model);
    }

    private IHypermedia? Hypermedia => HttpContext.RequestServices.GetService<IHypermedia>();
}
