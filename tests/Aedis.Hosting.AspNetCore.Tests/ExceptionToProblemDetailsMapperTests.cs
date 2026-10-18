using Aedis.Exceptions;
using Aedis.Hosting.AspNetCore.ErrorHandling;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante que o <see cref="ExceptionToProblemDetailsMapper" /> traduz cada família de exceção para o
///     status HTTP correto e que exceções inesperadas viram um 500 genérico, sem vazar a mensagem original
///     ao cliente (OWASP A05/A09).
/// </summary>
public sealed class ExceptionToProblemDetailsMapperTests
{
    private static readonly IProblemDetailsFactory Factory = new AedisProblemDetailsFactory();

    private static HttpContext Context() => new DefaultHttpContext();

    [Fact]
    public void BusinessException_usa_o_status_efetivo() {
        var exception = new BusinessException("registro em conflito", ViolationType.ConflictError);

        var problem = ExceptionToProblemDetailsMapper.Map(Context(), exception, Factory);

        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Detail.Should().Be("registro em conflito");
        problem.Extensions.Should().ContainKey("category");
    }

    [Fact]
    public void ValidationException_vira_422_com_erros_por_propriedade() {
        var exception = new ValidationException(new[] {
            new ValidationFailure("Nome", "é obrigatório"),
            new ValidationFailure("Email", "formato inválido")
        });

        var problem = ExceptionToProblemDetailsMapper.Map(Context(), exception, Factory);

        problem.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problem.Extensions.Should().ContainKey("errors");
    }

    [Fact]
    public void ForbiddenException_vira_403() {
        var problem = ExceptionToProblemDetailsMapper.Map(Context(), new ForbiddenException("sem permissão"), Factory);

        problem.Status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void Falta_de_autenticacao_vira_401() {
        var problem = ExceptionToProblemDetailsMapper.Map(Context(), new UnauthorizedAccessException("x"), Factory);

        problem.Status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Excecao_inesperada_vira_500_sem_vazar_a_mensagem() {
        var exception = new InvalidOperationException("detalhe interno sensível com segredo");

        var problem = ExceptionToProblemDetailsMapper.Map(Context(), exception, Factory);

        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().Be("Ocorreu um erro inesperado.");
        problem.Detail.Should().NotContain("segredo");
    }

    [Fact]
    public void ProblemDetails_carrega_traceId_para_correlacao() {
        var problem = ExceptionToProblemDetailsMapper.Map(Context(), new ForbiddenException("x"), Factory);

        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public void ServiceTemporarilyUnavailable_vira_503_com_titulo_padrao() {
        var problem = ExceptionToProblemDetailsMapper.Map(Context(),
            new ServiceTemporarilyUnavailableException("db", "indisponível"), Factory);

        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Title.Should().Be("Service Unavailable");
    }

    [Fact]
    public void Excecao_desconhecida_vira_500_sem_vazar_a_mensagem() {
        var problem = ExceptionToProblemDetailsMapper.Map(Context(), new InvalidOperationException("detalhe interno"), Factory);

        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().NotContain("detalhe interno");
    }

    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    [InlineData(412, "Precondition Failed")]
    [InlineData(422, "Unprocessable Entity")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(502, "Internal Server Error")]
    [InlineData(418, "Error")]
    public void StatusTitle_e_estavel_por_status(int status, string title) =>
        ExceptionToProblemDetailsMapper.StatusTitle(status).Should().Be(title);
}
