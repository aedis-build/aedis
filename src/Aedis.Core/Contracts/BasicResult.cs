namespace Aedis.Core;

public class BasicResult
{
    public bool Success { get; init; }
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public List<string> Messages { get; init; } = [];

    public static BasicResult Ok(Guid? correlationId = null) {
        var ok = new BasicResult {
            Success = true,
            CorrelationId = correlationId ?? Guid.NewGuid()
        };
        return ok;
    }

    public static BasicResult Error(Guid? correlationId = null, params string[] mensagens) {
        var error = new BasicResult {
            Success = false,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Messages = mensagens is { Length: > 0 } ? [..mensagens] : []
        };
        return error;
    }
}

public class BasicResult<T> : BasicResult
{
    public T? Result { get; init; }

    public static BasicResult<T> Ok(T result, Guid? correlationId = null) {
        var ok = new BasicResult<T> {
            Success = true,
            Result = result,
            CorrelationId = correlationId ?? Guid.NewGuid()
        };
        return ok;
    }

    public new static BasicResult<T> Error(Guid? correlationId = null, params string[] mensagens) {
        var error = new BasicResult<T> {
            Success = false,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Messages = mensagens is { Length: > 0 } ? [..mensagens] : []
        };
        return error;
    }
}