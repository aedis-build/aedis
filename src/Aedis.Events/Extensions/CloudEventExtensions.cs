using System.Text.Json;
using Aedis.Core.Utils;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Aedis.Events.Extensions;

/// <summary>
///     Extensões para criação e manipulação de ResourceCloudEvent.
/// </summary>
public static class CloudEventExtensions
{
    /// <summary>
    ///     Valida um ResourceCloudEvent e lança exceção se inválido.
    /// </summary>
    /// <param name="cloudEvent">Evento a ser validado</param>
    /// <exception cref="ArgumentException">Se o evento for inválido</exception>
    public static void Validate(this ResourceCloudEvent cloudEvent) {
        var result = CloudEventValidator.Validate(cloudEvent);
        result.ThrowIfInvalid();
    }

    /// <summary>
    ///     Cria um ResourceCloudEvent a partir dos parâmetros fornecidos.
    ///     Valida automaticamente o evento criado.
    /// </summary>
    /// <param name="id">ID único do evento (UUID)</param>
    /// <param name="source">Origem do evento (ex: "/aedis/notifications")</param>
    /// <param name="type">Tipo do evento (ex: "com.acme.order.created")</param>
    /// <param name="time">Timestamp UTC do evento (padrão: UtcNow)</param>
    /// <param name="subject">Identifica o alvo do evento (opcional)</param>
    /// <param name="data">Payload do evento (opcional)</param>
    /// <param name="test">Identifica se é evento de teste (opcional, null = real)</param>
    /// <returns>ResourceCloudEvent criado e validado</returns>
    /// <exception cref="ArgumentException">Se algum campo obrigatório estiver inválido</exception>
    public static ResourceCloudEvent CreateCloudEvent(
        Guid id,
        string source,
        string type,
        DateTimeOffset? time = null,
        string? subject = null,
        object? data = null,
        bool? test = null) {
        var cloudEvent = new ResourceCloudEvent {
            Id = id,
            Source = source ?? throw new ArgumentNullException(nameof(source)),
            Type = type ?? throw new ArgumentNullException(nameof(type)),
            Time = time ?? DateTimeOffset.UtcNow,
            Subject = subject,
            Data = data,
            Test = test
        };

        cloudEvent.Validate();
        return cloudEvent;
    }

    /// <summary>
    ///     Converte um ResourceCloudEvent para JSON string.
    ///     Usa opções de serialização padrão do framework (CamelCase, WhenWritingNull).
    /// </summary>
    /// <param name="cloudEvent">Evento a ser serializado</param>
    /// <param name="options">Opções de serialização (opcional)</param>
    /// <returns>JSON string do evento</returns>
    public static string ToJson(this ResourceCloudEvent cloudEvent, JsonSerializerOptions? options = null) {
        if (cloudEvent == null)
            throw new ArgumentNullException(nameof(cloudEvent));

        return JsonSerializer.Serialize(cloudEvent, options ?? SystemJsonOptionsFactory.Create());
    }

    /// <summary>
    ///     Converte um ResourceCloudEvent para UTF-8 bytes sem passar por string intermediária.
    ///     Economiza ~20 MB por chamada em relação a ToJson() para payloads de 20 MB:
    ///     evita a alocação UTF-16 (2 bytes/char) que ToJson() produz.
    ///     Use em conjunto com ByteArrayContent para HTTP POST sem cópias adicionais.
    /// </summary>
    /// <param name="cloudEvent">Evento a ser serializado</param>
    /// <param name="options">Opções de serialização (opcional)</param>
    /// <returns>UTF-8 bytes do JSON do evento</returns>
    public static byte[] ToJsonBytes(this ResourceCloudEvent cloudEvent, JsonSerializerOptions? options = null) {
        if (cloudEvent == null)
            throw new ArgumentNullException(nameof(cloudEvent));

        return JsonSerializer.SerializeToUtf8Bytes(cloudEvent, options ?? SystemJsonOptionsFactory.Create());
    }
}
