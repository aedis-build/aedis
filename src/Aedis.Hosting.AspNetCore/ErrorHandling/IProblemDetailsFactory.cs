using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aedis.Hosting.AspNetCore.ErrorHandling;

/// <summary>
///     Fábrica de <see cref="ProblemDetails" /> no formato RFC 9457, com o <c>traceId</c> da requisição já
///     preenchido (de <c>Activity.Current</c> ou do identificador de trace) e o <c>instance</c> apontando
///     para o caminho. Centraliza a forma das respostas de erro para que toda a plataforma responda de modo
///     consistente, sem vazar detalhes internos.
/// </summary>
public interface IProblemDetailsFactory
{
    /// <summary>
    ///     Cria um <see cref="ProblemDetails" /> para a resposta de erro atual, anexando <c>traceId</c> e
    ///     <c>instance</c> e quaisquer extensões informadas.
    /// </summary>
    /// <param name="context">Contexto HTTP da requisição que falhou.</param>
    /// <param name="statusCode">Status HTTP a retornar.</param>
    /// <param name="title">Título curto e estável do tipo de erro.</param>
    /// <param name="detail">Detalhe legível e seguro para o cliente; nunca a stack trace.</param>
    /// <param name="type">URI do tipo do problema; quando nulo, deriva do status.</param>
    /// <param name="extensions">Campos adicionais (ex.: <c>category</c>, <c>rule</c>, <c>errors</c>).</param>
    /// <returns>O <see cref="ProblemDetails" /> pronto para serialização.</returns>
    ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string? detail = null,
        string? type = null,
        IReadOnlyDictionary<string, object?>? extensions = null);
}
