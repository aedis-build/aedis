using System.Text.Json;
using Aedis.Events;
using Aedis.Events.Extensions;
using Aedis.Events.Serialization;
using FluentAssertions;
using Xunit;

namespace Aedis.Events.Tests;

/// <summary>
///     CloudEvents 1.0 do Aedis: o builder e a factory validam antes de entregar (id, source como caminho, type em
///     reverse-DNS, time, datacontenttype JSON), a validação descreve cada campo inválido, e o serializador JSON faz
///     o round-trip com os nomes da especificação (minúsculos) e omite <c>test</c> quando nulo.
/// </summary>
public sealed class CloudEventTests
{
    private static CloudEventBuilder Valid() => CloudEventBuilder.Create()
        .WithId(Guid.NewGuid()).WithSource("/orders").WithType("com.acme.order.created").WithTime(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Builder_monta_evento_valido_com_os_campos_informados() {
        var evt = Valid().WithSubject("order:1").WithData(new { total = 10 }).AsTest().Build();

        evt.SpecVersion.Should().Be("1.0");
        evt.Source.Should().Be("/orders");
        evt.Type.Should().Be("com.acme.order.created");
        evt.Subject.Should().Be("order:1");
        evt.Test.Should().BeTrue();
        evt.DataContentType.Should().Be("application/json");
        evt.Data.Should().NotBeNull();
    }

    [Fact]
    public void Builder_sem_time_usa_agora_e_AsReal_marca_test_false() {
        var before = DateTimeOffset.UtcNow;
        var evt = CloudEventBuilder.Create().WithId(Guid.NewGuid()).WithSource("/a").WithType("a.b").AsReal().Build();

        evt.Time.Should().BeOnOrAfter(before);
        evt.Test.Should().BeFalse();
        CloudEventBuilder.Create().WithId(Guid.NewGuid()).WithSource("/a").WithType("a.b").WithTest(null).Build().Test.Should().BeNull();
    }

    [Fact]
    public void Builder_rejeita_source_e_type_nulos_e_evento_invalido() {
        var act1 = () => CloudEventBuilder.Create().WithSource(null!);
        var act2 = () => CloudEventBuilder.Create().WithType(null!);
        var act3 = () => CloudEventBuilder.Create().WithSource("/a").WithType("a.b").Build();

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentException>().WithMessage("*id is required*");
    }

    [Fact]
    public void Validator_descreve_todos_os_campos_invalidos() {
        var evt = new ResourceCloudEvent { SpecVersion = "", Id = Guid.Empty, Source = "", Type = "", Time = default, DataContentType = "" };

        var result = CloudEventValidator.Validate(evt);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().ContainAll("specversion is required", "id is required", "source is required", "type is required", "time is required", "datacontenttype is required");
    }

    [Fact]
    public void Validator_rejeita_formatos_errados() {
        var evt = new ResourceCloudEvent { SpecVersion = "2.0", Id = Guid.NewGuid(), Source = "orders", Type = "created", Time = DateTimeOffset.UtcNow, DataContentType = "text/plain" };

        var result = CloudEventValidator.Validate(evt);

        result.ErrorMessage.Should().ContainAll("specversion must be '1.0'", "source must be a path", "type must follow reverse-DNS", "datacontenttype must be 'application/json'");
        var act = () => result.ThrowIfInvalid();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validator_aceita_evento_valido_e_nulo_e_falha() {
        CloudEventValidator.Validate(Valid().Build()).IsValid.Should().BeTrue();
        CloudEventValidator.Validate(null!).ErrorMessage.Should().Be("CloudEvent cannot be null");
        ValidationResult.Failure(null!).ErrorMessage.Should().Be("Validation failed");
        var act = () => ValidationResult.Success().ThrowIfInvalid();
        act.Should().NotThrow();
    }

    [Fact]
    public void Factory_cria_e_valida_e_rejeita_nulos() {
        var evt = CloudEventExtensions.CreateCloudEvent(Guid.NewGuid(), "/a", "a.b", subject: "s", data: 1, test: true);

        evt.Subject.Should().Be("s");
        evt.Time.Should().NotBe(default);
        var act1 = () => CloudEventExtensions.CreateCloudEvent(Guid.NewGuid(), null!, "a.b");
        var act2 = () => CloudEventExtensions.CreateCloudEvent(Guid.NewGuid(), "/a", null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToJson_usa_os_nomes_da_especificacao_e_omite_test_nulo() {
        var evt = Valid().WithData(new { orderId = 7 }).Build();

        var json = evt.ToJson();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("specversion").GetString().Should().Be("1.0");
        doc.RootElement.GetProperty("datacontenttype").GetString().Should().Be("application/json");
        doc.RootElement.TryGetProperty("test", out _).Should().BeFalse();
        evt.ToJsonBytes().Should().Equal(System.Text.Encoding.UTF8.GetBytes(json));
        var act1 = () => ((ResourceCloudEvent)null!).ToJson();
        var act2 = () => ((ResourceCloudEvent)null!).ToJsonBytes();
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Serializador_faz_round_trip_em_texto_e_bytes() {
        var serializer = new JsonCloudEventSerializer(null);
        var evt = Valid().WithSubject("x").Build();

        var back = serializer.Deserialize(serializer.Serialize(evt));
        var backBytes = serializer.DeserializeFromBytes(serializer.SerializeToBytes(evt));

        back!.Id.Should().Be(evt.Id);
        back.Subject.Should().Be("x");
        backBytes!.Type.Should().Be(evt.Type);
    }

    [Fact]
    public void Serializador_devolve_nulo_para_entrada_vazia_ou_invalida_e_rejeita_evento_nulo() {
        var serializer = new JsonCloudEventSerializer();

        serializer.Deserialize(" ").Should().BeNull();
        serializer.Deserialize("{ não é json").Should().BeNull();
        serializer.DeserializeFromBytes([]).Should().BeNull();
        serializer.DeserializeFromBytes(null!).Should().BeNull();
        var act1 = () => serializer.Serialize(null!);
        var act2 = () => serializer.SerializeToBytes(null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
