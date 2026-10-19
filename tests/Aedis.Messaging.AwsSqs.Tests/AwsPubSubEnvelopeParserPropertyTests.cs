using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace Aedis.Messaging.AwsSqs.Tests;

/// <summary>
///     Propriedades (FsCheck) do parser de envelopes SNS→SQS: corpos arbitrários nunca derrubam a detecção nem o
///     decodificador base64, e o que o SNS serializa é lido de volta sem perda para qualquer mensagem e atributo.
/// </summary>
public sealed class AwsPubSubEnvelopeParserPropertyTests {
    [Property]
    public bool IsSnsEnvelope_nunca_lanca_para_corpo_arbitrario(string body) {
        _ = AwsPubSubEnvelopeParser.IsSnsEnvelope(body);
        return true;
    }

    [Property]
    public bool TryFromBase64_nunca_lanca_e_so_devolve_bytes_de_base64_valido(string value) {
        var bytes = AwsPubSubEnvelopeParser.TryFromBase64(value);
        return bytes is null || Convert.FromBase64String(value).SequenceEqual(bytes);
    }

    [Property]
    public bool TryFromBase64_decodifica_o_que_o_Convert_codifica(byte[] payload) {
        var bytes = AwsPubSubEnvelopeParser.TryFromBase64(Convert.ToBase64String(payload));
        return payload.Length == 0 ? bytes is null : payload.SequenceEqual(bytes!);
    }

    [Property]
    public bool Parse_le_de_volta_mensagem_e_atributos_que_o_SNS_serializa(NonEmptyString message, NonEmptyString contentType, string? correlationId) {
        var body = JsonSerializer.Serialize(new {
            Type = "Notification",
            Message = message.Get,
            MessageAttributes = new Dictionary<string, object> {
                ["Content-Type"] = new { Type = "String", Value = contentType.Get },
                ["CorrelationId"] = new { Type = "String", Value = correlationId }
            }
        });

        var envelope = AwsPubSubEnvelopeParser.Parse(body);

        return AwsPubSubEnvelopeParser.IsSnsEnvelope(body)
               && envelope.Message == message.Get
               && envelope.ContentType == contentType.Get
               && envelope.CorrelationId == correlationId
               && envelope.ContentEncoding is null;
    }
}
