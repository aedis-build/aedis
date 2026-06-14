using Aedis.Events.Extensions;

namespace Aedis.Events;

/// <summary>
///     Builder fluente para construção de ResourceCloudEvent.
///     Facilita criação de eventos com validação automática.
/// </summary>
/// <example>
///     <code>
/// var cloudEvent = CloudEventBuilder
///     .Create()
///     .WithId(Guid.NewGuid())
///     .WithSource("/notifications")
///     .WithType("com.acme.order.created")
///     .WithSubject($"partner:{partnerId}:correlation:{correlationId}")
///     .WithTime(DateTimeOffset.UtcNow)
///     .WithData(payload)
///     .WithTest(false)
///     .Build();
/// </code>
/// </example>
public class CloudEventBuilder
{
    private object? _data;
    private Guid _id;
    private string _source = string.Empty;
    private string? _subject;
    private bool? _test;
    private DateTimeOffset? _time;
    private string _type = string.Empty;

    private CloudEventBuilder() { }

    /// <summary>
    ///     Cria uma nova instância do builder.
    /// </summary>
    /// <returns>Builder vazio</returns>
    public static CloudEventBuilder Create() {
        return new CloudEventBuilder();
    }

    /// <summary>
    ///     Define o ID único do evento (UUID).
    /// </summary>
    /// <param name="id">ID do evento</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithId(Guid id) {
        _id = id;
        return this;
    }

    /// <summary>
    ///     Define a origem do evento (ex: "/notifications").
    /// </summary>
    /// <param name="source">Origem do evento</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithSource(string source) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    /// <summary>
    ///     Define o tipo do evento (ex: "com.acme.order.created").
    /// </summary>
    /// <param name="type">Tipo do evento</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithType(string type) {
        _type = type ?? throw new ArgumentNullException(nameof(type));
        return this;
    }

    /// <summary>
    ///     Define o timestamp UTC do evento.
    ///     Se não especificado, usa DateTimeOffset.UtcNow no Build().
    /// </summary>
    /// <param name="time">Timestamp do evento</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithTime(DateTimeOffset time) {
        _time = time;
        return this;
    }

    /// <summary>
    ///     Define o subject (identifica o alvo do evento).
    /// </summary>
    /// <param name="subject">Subject do evento (opcional)</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithSubject(string? subject) {
        _subject = subject;
        return this;
    }

    /// <summary>
    ///     Define o payload do evento (objeto JSON).
    /// </summary>
    /// <param name="data">Payload do evento (opcional)</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithData(object? data) {
        _data = data;
        return this;
    }

    /// <summary>
    ///     Define se o evento é de teste.
    /// </summary>
    /// <param name="test">true = teste, false = real, null = não especificado</param>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder WithTest(bool? test) {
        _test = test;
        return this;
    }

    /// <summary>
    ///     Marca o evento como teste.
    /// </summary>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder AsTest() {
        _test = true;
        return this;
    }

    /// <summary>
    ///     Marca o evento como real (não teste).
    /// </summary>
    /// <returns>Builder para encadeamento</returns>
    public CloudEventBuilder AsReal() {
        _test = false;
        return this;
    }

    /// <summary>
    ///     Constrói o ResourceCloudEvent e valida automaticamente.
    /// </summary>
    /// <returns>ResourceCloudEvent validado</returns>
    /// <exception cref="ArgumentException">Se algum campo obrigatório estiver inválido</exception>
    public ResourceCloudEvent Build() {
        var cloudEvent = new ResourceCloudEvent {
            Id = _id,
            Source = _source,
            Type = _type,
            Time = _time ?? DateTimeOffset.UtcNow,
            Subject = _subject,
            Data = _data,
            Test = _test
        };

        cloudEvent.Validate();
        return cloudEvent;
    }
}