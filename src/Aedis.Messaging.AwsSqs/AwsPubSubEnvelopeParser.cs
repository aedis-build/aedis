using System.Text.Json;

namespace Aedis.Messaging.AwsSqs;

/// <summary>Conteúdo extraído de um envelope SNS→SQS: a mensagem interna (crua) e seu content-type.</summary>
public sealed record SnsSqsEnvelope(string Message, string? ContentType);

/// <summary>
///     Quando uma mensagem é entregue de um SNS Topic para uma SQS Queue, o corpo da SQS é um envelope
///     JSON do SNS. Este parser extrai a mensagem interna (sem decodificar) e o content-type a partir dos
///     <c>MessageAttributes</c>. A decodificação base64→bytes fica a cargo do consumer.
/// </summary>
public static class AwsPubSubEnvelopeParser
{
    /// <summary>Indica se o corpo é um envelope de notificação do SNS.</summary>
    public static bool IsSnsEnvelope(string sqsBody) {
        try {
            using var doc = JsonDocument.Parse(sqsBody);
            var root = doc.RootElement;
            return root.TryGetProperty("Type", out var type)
                   && type.GetString() == "Notification"
                   && root.TryGetProperty("Message", out _);
        }
        catch (JsonException) {
            return false;
        }
    }

    public static SnsSqsEnvelope Parse(string sqsBody) {
        using var doc = JsonDocument.Parse(sqsBody);
        var root = doc.RootElement;

        var rawMessage = root.GetProperty("Message").GetString()
                         ?? throw new InvalidOperationException("Envelope SNS sem o campo 'Message'.");

        string? contentType = null;
        if (root.TryGetProperty("MessageAttributes", out var attrs)
            && attrs.ValueKind == JsonValueKind.Object
            && attrs.TryGetProperty("ContentType", out var ct)
            && ct.ValueKind == JsonValueKind.Object
            && ct.TryGetProperty("Value", out var ctValue))
            contentType = ctValue.GetString();

        return new SnsSqsEnvelope(rawMessage, contentType);
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
}
