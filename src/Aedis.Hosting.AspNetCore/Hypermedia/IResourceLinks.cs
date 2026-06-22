using Microsoft.AspNetCore.Http;

namespace Aedis.Hosting.AspNetCore.Hypermedia;

/// <summary>
///     Fornece os links de hipermídia de um tipo de resposta. Implemente herdando de
///     <see cref="ResourceLinks{T}" /> (que dá o coletor fluente <see cref="ILinkMap" />) e registre com
///     <c>AddAedisHypermedia().Resource&lt;T, TLinks&gt;()</c>. A mesma implementação serve tanto ao recurso
///     único quanto a cada item de uma coleção.
/// </summary>
/// <typeparam name="T">Tipo do modelo de resposta.</typeparam>
public interface IResourceLinks<T> {
    /// <summary>
    ///     Encapsula o modelo em um <see cref="Resource{T}" /> e anexa seus links.
    /// </summary>
    /// <param name="model">Modelo de resposta a encapsular.</param>
    /// <param name="httpContext">Contexto da requisição atual.</param>
    Resource<T> Build(T model, HttpContext httpContext);
}
