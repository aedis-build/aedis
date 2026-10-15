using Microsoft.AspNetCore.Mvc.Routing;

namespace Aedis.Hosting.AspNetCore.Http;

/// <summary>
///     Marca uma action que responde ao método HTTP <c>QUERY</c> (consulta com corpo): filtros complexos
///     viajam em JSON no corpo, sem os limites e o vazamento de PII da query string. O ASP.NET Core 10 ainda não
///     traz o atributo nativo; este antecipa a ergonomia sobre a mesma base (<see cref="HttpMethodAttribute" />)
///     que a implementação oficial usa, então o roteamento e o <c>[FromBody]</c> funcionam normalmente.
/// </summary>
/// <example>
///     <code>
///     [HttpQuery]
///     public Task&lt;IActionResult&gt; Search([FromBody] SearchOrdersRequest request) { ... }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpQueryAttribute : HttpMethodAttribute
{
    /// <summary>Nome do método HTTP.</summary>
    public const string Method = "QUERY";

    private static readonly IEnumerable<string> SupportedMethods = [Method];

    /// <summary>Cria o atributo para o método <c>QUERY</c> sem template de rota.</summary>
    public HttpQueryAttribute() : base(SupportedMethods) { }

    /// <summary>Cria o atributo para o método <c>QUERY</c> com o template de rota informado.</summary>
    /// <param name="template">Template de rota da action.</param>
    public HttpQueryAttribute(string template) : base(SupportedMethods, template) { }
}
