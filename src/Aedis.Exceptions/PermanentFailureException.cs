namespace Aedis.Exceptions;

/// <summary>
///     Exceção base para erros permanentes que não devem ser retentados.
///     Comportamento no RabbitMQ:
///     - Se EnableFinalDLQ = true: Envia DIRETO para DLQ (NACK requeue=false)
///     - Se EnableFinalDLQ = false: Descarta com ACK
///     NÃO passa por health retry nem retry com backoff.
///     Exemplos de uso: validação de negócio falhou, esquema inválido, dados corrompidos.
/// </summary>
public abstract class PermanentFailureException : Exception
{
    /// <summary>Cria a exceção com a mensagem que descreve a falha permanente.</summary>
    protected PermanentFailureException(string message) : base(message) { }

    /// <summary>Cria a exceção encadeando a causa original (<paramref name="innerException" />).</summary>
    protected PermanentFailureException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Sempre falso por padrão: erros permanentes não são recolocados na fila.</summary>
    public virtual bool ShouldRequeue => false;

    /// <summary>Verdadeiro por padrão: a mensagem segue para a DLQ quando habilitada.</summary>
    public virtual bool SendToDeadLetterQueue => true;

    /// <summary>Dados extras anexados ao erro para contexto e logging.</summary>
    public Dictionary<string, object> ErrorData { get; } = new();

    /// <summary>Anexa um par chave/valor a <see cref="ErrorData" /> para enriquecer o contexto do erro.</summary>
    public void AddErrorData(string key, object value) {
        ErrorData[key] = value;
    }
}