using Aedis.App1.Api.Dtos.Requests;
using Aedis.App1.Api.Dtos.Responses;
using Aedis.App1.Api.Mappers;
using Aedis.App1.Application.Products.Commands;
using Aedis.App1.Application.Products.Queries;
using Aedis.Commands.Abstractions;
using Aedis.Hosting.AspNetCore.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.App1.Api.Controllers;

/// <summary>
///     Endpoints REST do agregado Product. O controller é fino: delega a regra aos handlers via
///     <c>CommandExecutor</c> e devolve respostas HATEOAS com os códigos de status RESTful —
///     <c>201</c> + <c>Location</c> na criação, <c>200</c> com links na leitura/atualização, <c>204</c> na
///     remoção. Erros de negócio (404/409) e de validação (422) são traduzidos pelo middleware do framework.
/// </summary>
[Authorize]
[Route("v1/products")]
public sealed class ProductsController : ApiControllerBase {
    private readonly ProductMapper _mapper;

    /// <summary>
    ///     Cria o controller com o executor CQRS e o mapper de saída.
    /// </summary>
    /// <param name="commandExecutor">Executor de comandos/consultas.</param>
    /// <param name="mapper">Mapper de entidade para resposta.</param>
    public ProductsController(ICommandExecutor commandExecutor, ProductMapper mapper) : base(commandExecutor) {
        _mapper = mapper;
    }

    /// <summary>
    ///     Cria um produto. Retorna <c>201 Created</c> com o header <c>Location</c> apontando para o recurso.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken) {
        var product = await CommandExecutor.ExecuteAsync(new CreateProductCommand(request.Code, request.Name, request.Price), cancellationToken);
        return CreatedWithHateoas(nameof(GetById), new { id = product.Id }, _mapper.ToResponse(product));
    }

    /// <summary>
    ///     Recupera um produto pela identidade. Retorna <c>200</c> com links ou <c>404</c> quando não existe.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) {
        var product = await CommandExecutor.ExecuteAsync(new GetProductByIdQuery(id), cancellationToken);
        return OkWithHateoas(product is null ? null : _mapper.ToResponse(product));
    }

    /// <summary>
    ///     Lista produtos de forma paginada, com filtros opcionais por código e nome. Retorna <c>200</c> com a
    ///     coleção e os links de paginação.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string? code, [FromQuery] string? name, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) {
        var result = await CommandExecutor.ExecuteAsync(new SearchProductsQuery(code, name, page, pageSize), cancellationToken);
        var items = result.Items.Select(_mapper.ToResponse);
        return CollectionWithHateoas(items, result.Page, result.PageSize, result.Total);
    }

    /// <summary>
    ///     Atualiza um produto existente. Retorna <c>200</c> com o recurso atualizado ou <c>404</c> quando não
    ///     existe.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken) {
        var product = await CommandExecutor.ExecuteAsync(new UpdateProductCommand(id, request.Name, request.Price), cancellationToken);
        return OkWithHateoas(_mapper.ToResponse(product));
    }

    /// <summary>
    ///     Remove um produto. Retorna <c>204 No Content</c> ou <c>404</c> quando não existe.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) {
        await CommandExecutor.ExecuteAsync(new DeleteProductCommand(id), cancellationToken);
        return NoContent();
    }
}
