using System.Text;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
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
///     Partes puras do provider AWS (sem container): envelope SNS→SQS (só <c>Type=Notification</c>; atributos
///     com e sem hífen; trace), codec de transporte (base64/identity/detecção, gzip com guarda de descompressão),
///     nomes (normalização preservando <c>.fifo</c>, nome efetivo com <c>UseFifoQueues</c>, DLQ FIFO), detecção
///     de exchange sem sondar quando <c>UseTopics</c>, e a extensão de DI.
/// </summary>
public sealed class AwsSqsUnitTests
{
    private static AwsPubSubFactory Factory(bool useFifo = false, bool useTopics = true) => new(
        Options.Create(new AwsSqsOptions { Region = "us-east-1", UseFifoQueues = useFifo, UseTopics = useTopics }),
        NullLogger<AwsPubSubFactory>.Instance);

    [Fact]
    public void IsSnsEnvelope_so_reconhece_notificacao_do_sns() {
        AwsPubSubEnvelopeParser.IsSnsEnvelope("""{"Type":"Notification","Message":"abc","MessageAttributes":{}}""").Should().BeTrue();
        AwsPubSubEnvelopeParser.IsSnsEnvelope("""{"Type":"SubscriptionConfirmation","Message":"x"}""").Should().BeFalse();
        AwsPubSubEnvelopeParser.IsSnsEnvelope("""{"Message":"webhook sem Type"}""").Should().BeFalse();
        AwsPubSubEnvelopeParser.IsSnsEnvelope("""{"orderId":42}""").Should().BeFalse();
        AwsPubSubEnvelopeParser.IsSnsEnvelope("texto cru").Should().BeFalse();
    }

    [Fact]
    public void Parse_extrai_transporte_correlacao_e_trace_aceitando_nomes_sem_hifen() {
        var body = """
        {"Type":"Notification","Message":"cGF5bG9hZA==",
         "MessageAttributes":{"ContentType":{"Type":"String","Value":"application/json"},
                              "Content-Encoding":{"Type":"String","Value":"gzip"},
                              "Content-Transfer-Encoding":{"Type":"String","Value":"base64"},
                              "CorrelationId":{"Type":"String","Value":"corr-1"},
                              "traceparent":{"Type":"String","Value":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"}}}
        """;

        var envelope = AwsPubSubEnvelopeParser.Parse(body);

        envelope.Message.Should().Be("cGF5bG9hZA==");
        envelope.ContentType.Should().Be("application/json");
        envelope.ContentEncoding.Should().Be("gzip");
        envelope.ContentTransferEncoding.Should().Be("base64");
        envelope.CorrelationId.Should().Be("corr-1");
        envelope.TraceParent.Should().StartWith("00-0af7651916cd43dd8448eb211c80319c");
        envelope.TraceState.Should().BeNull();
    }

    [Fact]
    public void TryFromBase64_decodifica_base64_e_rejeita_json_cru() {
        AwsPubSubEnvelopeParser.TryFromBase64(Convert.ToBase64String("dados"u8.ToArray()))
            .Should().Equal("dados"u8.ToArray());

        AwsPubSubEnvelopeParser.TryFromBase64("""{"a":1}""").Should().BeNull("JSON cru não é base64");
    }

    [Fact]
    public void Codec_decodifica_base64_identity_e_detecta_sem_transfer_encoding() {
        var encoders = MessageEncoderResolver.CreateDefault();
        var raw = "{\"a\":1}"u8.ToArray();

        AwsMessageTransportCodec.Decode(Convert.ToBase64String(raw), "base64", null, encoders).Should().Equal(raw);
        AwsMessageTransportCodec.Decode("{\"a\":1}", "identity", null, encoders).Should().Equal(raw);
        AwsMessageTransportCodec.Decode("{\"a\":1}", null, null, encoders).Should().Equal(raw, "JSON cru de produtor externo passa como UTF-8");
        AwsMessageTransportCodec.Decode(Convert.ToBase64String(raw), null, null, encoders).Should().Equal(raw, "base64 sem atributo é detectado");
    }

    [Fact]
    public void Codec_reverte_gzip_e_rejeita_payload_acima_do_limite() {
        var gzip = new GzipMessageEncoder();
        var payload = Encoding.UTF8.GetBytes(new string('x', 10_000));
        var compressed = Convert.ToBase64String(gzip.Encode(payload).ToArray());

        var ok = MessageEncoderResolver.CreateDefault();
        AwsMessageTransportCodec.Decode(compressed, "base64", "gzip", ok).Should().Equal(payload);

        var limited = new MessageEncoderResolver([new IdentityMessageEncoder(), new GzipMessageEncoder(1024)]);
        var act = () => AwsMessageTransportCodec.Decode(compressed, "base64", "gzip", limited);

        act.Should().Throw<InvalidOperationException>().WithMessage("*excede o limite de 1024 bytes*");
    }

    [Fact]
    public void Codec_rejeita_encodings_desconhecidos() {
        var encoders = MessageEncoderResolver.CreateDefault();

        var transfer = () => AwsMessageTransportCodec.Decode("x", "quoted-printable", null, encoders);
        var content = () => AwsMessageTransportCodec.Decode("eA==", "base64", "br", encoders);

        transfer.Should().Throw<NotSupportedException>();
        content.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("My Queue!", "my-queue")]
    [InlineData("Order.Created", "order-created")]
    [InlineData("  spaced  name  ", "spaced-name")]
    [InlineData("Orders.FIFO", "orders.fifo")]
    public void NormalizeName_segue_convencoes_aws_preservando_fifo(string input, string expected) {
        Factory().NormalizeName(input).Should().Be(expected);
    }

    [Fact]
    public void ResolveQueueName_aplica_fifo_quando_configurado_sem_duplicar() {
        Factory(useFifo: true).ResolveQueueName("orders").Should().Be("orders.fifo");
        Factory(useFifo: true).ResolveQueueName("orders.fifo").Should().Be("orders.fifo");
        Factory().ResolveQueueName("orders").Should().Be("orders");
    }

    [Fact]
    public void Nome_da_dlq_fifo_mantem_o_sufixo_no_fim() {
        AwsSqsAdministrationHelper.DeadLetterName("orders.fifo", true).Should().Be("orders-dlq.fifo");
        AwsSqsAdministrationHelper.DeadLetterName("orders", false).Should().Be("orders-dlq");
    }

    [Fact]
    public void IsFifoQueue_detecta_sufixo_fifo() {
        var factory = Factory();
        factory.IsFifoQueue("orders.fifo").Should().BeTrue();
        factory.IsFifoQueue("orders").Should().BeFalse();
    }

    [Fact]
    public async Task Detect_com_UseTopics_resolve_Topic_sem_sondar_o_sqs() {
        var factory = Factory(useTopics: true);

        var type = await factory.DetectExchangeTypeAsync("orders");

        type.Should().Be(AwsSqsBaseService.ExchangeType.Topic, "nenhum cliente é criado nem GetQueueUrl chamado");
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Aws:Region"] = "us-east-1",
            ["Aws:UseTopics"] = "true",
            ["Aws:MaxNumberOfMessages"] = "10",
            ["Aws:MaxDecompressedPayloadBytes"] = "2048"
        }).Build();

    [Fact]
    public void AddAedisAwsSqs_vincula_options_e_registra_broker_keyed() {
        var services = new ServiceCollection().AddLogging().AddAedisAwsSqs(Config());
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AwsSqsOptions>>().Value;
        options.Region.Should().Be("us-east-1");
        options.VisibilityTimeout.Should().Be(60);
        options.MaxDecompressedPayloadBytes.Should().Be(2048);
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
