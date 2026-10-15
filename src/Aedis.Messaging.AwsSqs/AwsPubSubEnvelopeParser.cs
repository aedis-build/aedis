using System.Text.Json;

namespace Aedis.Messaging.AwsSqs;

/// <summary>
///     Conteúdo extraído de um envelope SNS→SQS: a mensagem interna (crua, sem decodificar) e os atributos de
///     transporte que viajaram no envelope — content-type, content-encoding, content-transfer-encoding,
///     correlação e contexto de trace W3C.
/// </summary>
public sealed record SnsSqsEnvelope(
    string Message,
    string? ContentType,
    string? ContentEncoding,
    string? ContentTransferEncoding = null,
    string? CorrelationId = null,
    string? TraceParent = null,
    string? TraceState = null);

/// <summary>
///     Quando uma mensagem é entregue de um SNS Topic para uma SQS Queue, o corpo da SQS é um envelope JSON
///     do SNS (<c>Type = Notification</c>). Este parser reconhece só esse envelope — um JSON cru de um
///     produtor externo (webhook) não é desembrulhado — e extrai a mensagem interna e os atributos de
///     transporte dos <c>MessageAttributes</c>, aceitando os nomes com e sem hífen. A decodificação
///     base64/gzip fica a cargo do consumer.
/// </summary>
public static class AwsPubSubEnvelopeParser
{
    /// <summary>Indica se o corpo é um envelope de notificação do SNS (<c>Type == "Notification"</c> com <c>Message</c>).</summary>
    public static bool IsSnsEnvelope(string sqsBody) {
        if (string.IsNullOrWhiteSpace(sqsBody)) return false;

        try {
            using var doc = JsonDocument.Parse(sqsBody);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("Type", out var type)
                   && type.ValueKind == JsonValueKind.String
                   && type.GetString() == "Notification"
                   && root.TryGetProperty("Message", out _);
        }
        catch (JsonException) {
            return false;
        }
    }

    /// <summary>
    ///     Extrai do envelope SNS a mensagem interna (crua) e os atributos de transporte. Use após
    ///     <see cref="IsSnsEnvelope" /> confirmar que o corpo é um envelope.
    /// </summary>
    public static SnsSqsEnvelope Parse(string sqsBody) {
        using var doc = JsonDocument.Parse(sqsBody);
        var root = doc.RootElement;

        var rawMessage = root.GetProperty("Message").GetString()
                         ?? throw new InvalidOperationException("Envelope SNS sem o campo 'Message'.");

        JsonElement? attributes = root.TryGetProperty("MessageAttributes", out var attrs)
                                  && attrs.ValueKind == JsonValueKind.Object
            ? attrs
            : null;

        return new SnsSqsEnvelope(
            rawMessage,
            ReadEnvelopeAttribute(attributes, "Content-Type", "ContentType"),
            ReadEnvelopeAttribute(attributes, "Content-Encoding", "ContentEncoding"),
            ReadEnvelopeAttribute(attributes, "Content-Transfer-Encoding", "ContentTransferEncoding"),
            ReadEnvelopeAttribute(attributes, "CorrelationId"),
            ReadEnvelopeAttribute(attributes, "traceparent"),
            ReadEnvelopeAttribute(attributes, "tracestate"));
    }

    /// <summary>Decodifica base64→bytes se a string for base64 válido; senão devolve null.</summary>
    public static byte[]? TryFromBase64(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 4 != 0)
            return null;

        foreach (var c in value) {
            var ok = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=';
            if (!ok) return null;
        }

        try {
            return Convert.FromBase64String(value);
        }
        catch {
            return null;
        }
    }

    private static string? ReadEnvelopeAttribute(JsonElement? attributes, params string[] names) {
        if (attributes is not { } attrs) return null;

        foreach (var name in names)
            if (attrs.TryGetProperty(name, out var attr)
                && attr.ValueKind == JsonValueKind.Object
                && attr.TryGetProperty("Value", out var value)
                && value.ValueKind == JsonValueKind.String)
                return value.GetString();

        return null;
    }
}
