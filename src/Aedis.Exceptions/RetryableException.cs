namespace Aedis.Exceptions;

/// <summary>
///     Exceção base para erros que podem ser retentados.
///     Não incrementa death count quando tratada com health retry.
/// </summary>
public abstract class RetryableException : Exception
{
    protected RetryableException(string message) : base(message) { }

    protected RetryableException(string message, Exception innerException) : base(message, innerException) { }

    protected RetryableException(string message, TimeSpan retryAfter) : base(message) {
        RetryAfter = retryAfter;
    }

    public virtual bool ShouldRequeue => true;
    public virtual TimeSpan? RetryAfter { get; }
}