using Aedis.Messaging.Abstractions;
using Aedis.Messaging.AwsSqs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.AwsSqs.Tests;

/// <summary>
///     Partes puras do provider AWS (sem container): parsing do envelope SNS→SQS, base64, normalização de
///     nomes, detecção FIFO e a extensão de DI.
/// </summary>
public sealed class AwsSqsUnitTests
{
    private static AwsPubSubFactory Factory() => new(
        Options.Create(new AwsSqsOptions { Region = "us-east-1" }), NullLogger<AwsPubSubFactory>.Instance);

    [Fact]
    public void IsSnsEnvelope_detecta_envelope_de_notificacao() {
        var envelope = """{"Type":"Notification","Message":"abc","MessageAttributes":{}}""";
        var direct = """{"orderId":42}""";

        AwsPubSubEnvelopeParser.IsSnsEnvelope(envelope).Should().BeTrue();
        AwsPubSubEnvelopeParser.IsSnsEnvelope(direct).Should().BeFalse();
    }

    [Fact]
    public void Parse_extrai_mensagem_interna_e_content_type() {
        var body = """
        {"Type":"Notification","Message":"cGF5bG9hZA==",
         "MessageAttributes":{"ContentType":{"Type":"String","Value":"application/json"}}}
        """;

        var envelope = AwsPubSubEnvelopeParser.Parse(body);

        envelope.Message.Should().Be("cGF5bG9hZA==");
        envelope.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void TryFromBase64_decodifica_base64_e_rejeita_json_cru() {
        AwsPubSubEnvelopeParser.TryFromBase64(Convert.ToBase64String("dados"u8.ToArray()))
            .Should().Equal("dados"u8.ToArray());

        AwsPubSubEnvelopeParser.TryFromBase64("""{"a":1}""").Should().BeNull("JSON cru não é base64");
    }

    [Theory]
    [InlineData("My Queue!", "my-queue")]
    [InlineData("Order.Created", "order-created")]
    [InlineData("  spaced  name  ", "spaced-name")]
    public void NormalizeName_segue_convencoes_aws(string input, string expected) {
        Factory().NormalizeName(input).Should().Be(expected);
    }

    [Fact]
    public void IsFifoQueue_detecta_sufixo_fifo() {
        var factory = Factory();
        factory.IsFifoQueue("orders.fifo").Should().BeTrue();
        factory.IsFifoQueue("orders").Should().BeFalse();
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Aws:Region"] = "us-east-1",
            ["Aws:UseTopics"] = "true",
            ["Aws:MaxNumberOfMessages"] = "10"
        }).Build();

    [Fact]
    public void AddAedisAwsSqs_vincula_options_e_registra_broker_keyed() {
        var services = new ServiceCollection().AddLogging().AddAedisAwsSqs(Config());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AwsSqsOptions>>().Value.Region.Should().Be("us-east-1");
        services.Should().Contain(d => d.ServiceType == typeof(IMessageBrokerService));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IMessageBrokerService) && d.IsKeyedService && Equals(d.ServiceKey, "awssqs"));
    }

    [Fact]
    public void AddAedisAwsSqs_registra_health_check_como_ready() {
        var provider = new ServiceCollection().AddLogging().AddAedisAwsSqs(Config()).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should().ContainSingle(r => r.Name == "awssqs").Subject;

        registration.Tags.Should().Contain("ready");
    }
}
