using System.Diagnostics;

namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Base prática para mensagens de domínio: já preenche <see cref="CorrelationId" /> (do trace atual ou um
///     GUID) e <see cref="Date" />, restando à subclasse declarar <see cref="EventName" />. Herde para criar
///     mensagens publicáveis; sobrescreva <see cref="ToData" /> apenas quando o payload diferir do envelope.
/// </summary>
public abstract class MessageBase : IMessage
{
    /// <summary>
    ///     Correlação da mensagem. Por padrão usa o <c>TraceId</c> da <see cref="Activity" /> corrente (para
    ///     propagar o trace distribuído) e, na ausência, um GUID novo.
    /// </summary>
    public string CorrelationId { get; set; } = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();

    /// <summary>Momento de criação da mensagem; inicializado com o instante UTC atual.</summary>
    public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Nome do evento; cada subclasse o define conforme o tipo de mensagem que representa.</summary>
    public abstract string EventName { get; }

    /// <summary>Retorna o payload a serializar; por padrão é a própria instância (envelope = payload).</summary>
    public virtual object ToData() {
        return this;
    }
}