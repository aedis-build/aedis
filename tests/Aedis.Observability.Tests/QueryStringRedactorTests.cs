using Aedis.Observability.Serilog;
using FluentAssertions;
using Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Garante que a ofuscação de query string mascara os valores de parâmetros sensíveis (segredos e PII) e
///     preserva os demais — o tratamento do vetor de vazamento em access-logs (<c>?access_token=…</c>).
/// </summary>
public sealed class QueryStringRedactorTests {
    private static readonly RedactionOptions Options = new();

    [Fact]
    public void Mascara_sensiveis_e_preserva_o_resto() {
        var redacted = QueryStringRedactor.Redact("?access_token=abc.def&page=2&cpf=12345678901", Options);

        redacted.Should().Be("?access_token=***&page=2&cpf=***");
    }

    [Fact]
    public void Sem_parametro_sensivel_fica_inalterado() {
        QueryStringRedactor.Redact("?page=2&size=10", Options).Should().Be("?page=2&size=10");
    }

    [Fact]
    public void Entrada_vazia_retorna_vazio() {
        QueryStringRedactor.Redact("", Options).Should().BeEmpty();
        QueryStringRedactor.Redact(null, Options).Should().BeEmpty();
    }
}
