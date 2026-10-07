using Aedis.Hosting.AspNetCore.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>
///     Garante que o mapa <c>errors</c> do 422 neutraliza mensagens que vazam o tipo CLR, descarta o
///     <c>"field is required"</c> espúrio quando o corpo falhou e preserva validações legítimas.
/// </summary>
public class ModelStateSanitizerTests
{
    private const string Neutral = "The value provided for '{0}' is not valid.";

    [Fact]
    public void Mensagem_com_System_ponto_e_neutralizada() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.status",
            "The JSON value could not be converted to System.Nullable`1[Minha.Api.Status]. Path: $.status");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors.Should().ContainKey("status")
            .WhoseValue.Should().Equal(string.Format(Neutral, "status"));
        errors["status"].Should().NotContain(message => message.Contains("System."));
    }

    [Fact]
    public void Mensagem_could_not_be_converted_e_neutralizada_sem_depender_da_caixa() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.valor", "The value COULD NOT BE CONVERTED.");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors["valor"].Should().Equal(string.Format(Neutral, "valor"));
    }

    [Fact]
    public void JsonException_sem_mensagem_vira_texto_neutro() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.data", string.Empty);

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors["data"].Should().Equal(string.Format(Neutral, "data"));
    }

    [Fact]
    public void Required_espurio_e_descartado_quando_corpo_falhou() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$", "The JSON value could not be converted to Minha.Api.Request.");
        modelState.AddModelError("request", "The request field is required.");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors.Should().ContainKey("body").WhoseValue.Should().Equal(string.Format(Neutral, "body"));
        errors.Should().NotContainKey("request");
    }

    [Fact]
    public void Required_legitimo_e_preservado_quando_corpo_nao_falhou() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Nome", "The Nome field is required.");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors["Nome"].Should().Equal("The Nome field is required.");
    }

    [Fact]
    public void Mensagem_de_validacao_legitima_e_preservada() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.email", "Email inválido.");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors["email"].Should().Equal("Email inválido.");
    }

    [Fact]
    public void Mensagens_repetidas_sao_colapsadas() {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.a", "x could not be converted to System.Int32");
        modelState.AddModelError("$.a", "y could not be converted to System.Int32");

        var errors = ModelStateSanitizer.SanitizeModelState(modelState);

        errors["a"].Should().ContainSingle();
    }

    [Theory]
    [InlineData("could not be converted", true)]
    [InlineData("contém System.String", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Campo obrigatório.", false)]
    public void IsLeakedMessage_classifica_corretamente(string message, bool esperado) {
        ModelStateSanitizer.IsLeakedMessage(message).Should().Be(esperado);
    }
}
