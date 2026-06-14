namespace Aedis.Exceptions;

/// <summary>
///     Exceção lançada quando uma operação não pode ser processada devido a restrições de agendamento/grade horária.
///     Faz com que a mensagem aguarde na fila de health retry sem incrementar death count.
///     Útil para operações que só podem ser executadas em janelas de tempo específicas.
/// </summary>
public class BusinessScheduleException : RetryableException
{
    public BusinessScheduleException(string message, string scheduleWindow, DateTimeOffset currentTime)
        : base(message) {
        ScheduleWindow = scheduleWindow;
        CurrentTime = currentTime;
    }

    public BusinessScheduleException(string message, string scheduleWindow, DateTimeOffset currentTime,
        TimeSpan retryAfter)
        : base(message, retryAfter) {
        ScheduleWindow = scheduleWindow;
        CurrentTime = currentTime;
    }

    /// <summary>
    ///     Janela de processamento configurada (ex: "08:00-18:00 BRT")
    /// </summary>
    public string ScheduleWindow { get; }

    /// <summary>
    ///     Horário atual da tentativa de processamento
    /// </summary>
    public DateTimeOffset CurrentTime { get; }
}