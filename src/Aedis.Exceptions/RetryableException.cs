namespace Aedis.Exceptions;

/// <summary>
///     Exceção base para erros que podem ser retentados.
///     Não incrementa death count quando tratada com health retry.
/// </summary>
public abstract class RetryableException : Exception
{
    /// <summary>Cria a exceção com a mensagem que descreve a falha recuperável.</summary>
    protected RetryableException(string message) : base(message) { }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />).</summary>
    protected RetryableException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Cria a exceção definindo o <paramref name="retryAfter" /> — atraso sugerido antes da próxima tentativa.</summary>
    protected RetryableException(string message, TimeSpan retryAfter) : base(message) {
        RetryAfter = retryAfter;
    }

    /// <summary>Verdadeiro por padrão: a mensagem deve ser recolocada na fila para nova tentativa.</summary>
    public virtual bool ShouldRequeue => true;

    /// <summary>Atraso sugerido antes da próxima tentativa, quando definido; nulo deixa a estratégia a cargo do consumidor.</summary>
    public virtual TimeSpan? RetryAfter { get; }
}