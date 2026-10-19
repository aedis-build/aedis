using Aedis.Hosting.AspNetCore.ErrorHandling;
using FluentAssertions;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante que valores vindos da requisição não forjam linhas de log: quebras de linha viram escapes visíveis,
///     demais caracteres de controle somem e valores normais passam intactos.
/// </summary>
public sealed class LogValueSanitizerTests {
    [Theory]
    [InlineData("/orders/42", "/orders/42")]
    [InlineData("/a\nERROR forged\r\n", "/a\\nERROR forged\\r\\n")]
    [InlineData("/a\u0000b\u001bc", "/abc")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Neutraliza_quebras_de_linha_e_controles(string? value, string expected) {
        LogValueSanitizer.Sanitize(value).Should().Be(expected);
    }
}
