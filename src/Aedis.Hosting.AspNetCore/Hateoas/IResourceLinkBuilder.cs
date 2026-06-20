using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hateoas;

/// <summary>
///     Constrói o recurso HATEOAS de item único a partir de um modelo de resposta, anexando os links
///     pertinentes (<c>self</c>, <c>update</c>, <c>delete</c>, etc.). Cada tipo de resposta fornece sua própria
///     implementação, registrada via <c>AddAedisResourceLinks</c>. O <see cref="HttpContext" /> dá acesso à URL
///     base e ao usuário atual, permitindo links sensíveis ao contexto (por exemplo, exibir <c>delete</c> apenas
///     para quem tem permissão).
/// </summary>
/// <typeparam name="T">Tipo do modelo de resposta.</typeparam>
public interface IResourceLinkBuilder<T> {
    /// <summary>
    ///     Encapsula o modelo em um <see cref="Resource{T}" /> e anexa seus links.
    /// </summary>
    /// <param name="model">Modelo de resposta a encapsular.</param>
    /// <param name="httpContext">Contexto da requisição atual (URL base, usuário, rota).</param>
    Resource<T> Build(T model, HttpContext httpContext);
}

/// <summary>
///     Constrói o recurso HATEOAS de coleção paginada, anexando os links de navegação entre páginas. A
///     implementação padrão é <see cref="DefaultCollectionLinkBuilder{T}" />, registrada via
///     <c>AddAedisCollectionLinks</c> com o caminho base da coleção.
/// </summary>
/// <typeparam name="T">Tipo de cada item da coleção.</typeparam>
public interface ICollectionLinkBuilder<T> {
    /// <summary>
    ///     Encapsula os itens em uma <see cref="CollectionResource{T}" /> e anexa os links de paginação.
    /// </summary>
    /// <param name="items">Itens da página atual.</param>
    /// <param name="httpContext">Contexto da requisição atual.</param>
    /// <param name="page">Número da página atual (1-based), quando aplicável.</param>
    /// <param name="pageSize">Quantidade de itens por página, quando aplicável.</param>
    /// <param name="totalCount">Total de itens em todas as páginas, quando conhecido.</param>
    CollectionResource<T> Build(IEnumerable<T> items, HttpContext httpContext, int? page = null, int? pageSize = null, int? totalCount = null);
}
