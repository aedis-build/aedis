namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Política de resiliência de um consumer: habilita (ou não) health-retry, backoff e dead-letter, e
///     parametriza limites e atrasos. Passe ao assinar uma fila para escolher como falhas são tratadas.
///     Use as fábricas (<see cref="None" />, <see cref="All" />, <see cref="HealthOnly" />,
///     <see cref="WithBackoff" />) para os cenários comuns em vez de montar a instância à mão.
/// </summary>
public class ConsumerRetryOptions
{
    /// <summary>
    ///     Ativa o reprocesso por health-retry: mensagens de erros transitórios voltam a uma fila de espera
    ///     com TTL e são reentregues após o atraso, dando tempo para a dependência se recuperar.
    /// </summary>
    public bool EnableHealthRetry { get; set; }

    /// <summary>
    ///     Ativa o retry com backoff: cada tentativa aguarda <see cref="BackoffDelaySeconds" /> antes de
    ///     reentregar, incrementando a contagem de mortes até <see cref="MaxRetries" />.
    /// </summary>
    public bool EnableRetryWithBackoff { get; set; }

    /// <summary>
    ///     Ativa o encaminhamento para a dead-letter queue final quando a mensagem excede
    ///     <see cref="MaxRetries" /> ou falha permanentemente.
    /// </summary>
    public bool EnableDeadLetter { get; set; }

    /// <summary>Número máximo de tentativas antes de enviar a mensagem à DLQ.</summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>Atraso (em segundos) entre tentativas no modo backoff.</summary>
    public int BackoffDelaySeconds { get; set; } = 900;

    /// <summary>Atraso (em segundos) que a mensagem aguarda na fila de health-retry antes de reentrega.</summary>
    public int HealthCheckRetryDelaySeconds { get; set; } = 300;

    /// <summary>Política sem resiliência: nenhum retry e sem DLQ. Falhas resultam em requeue imediato.</summary>
    public static ConsumerRetryOptions None() {
        return new ConsumerRetryOptions();
    }

    /// <summary>Política completa: health-retry, backoff e dead-letter habilitados.</summary>
    public static ConsumerRetryOptions All(
        int maxRetries = 10,
        int backoffDelaySeconds = 900,
        int healthCheckRetryDelaySeconds = 300) {
        return new ConsumerRetryOptions {
            EnableHealthRetry = true,
            EnableRetryWithBackoff = true,
            EnableDeadLetter = true,
            MaxRetries = maxRetries,
            BackoffDelaySeconds = backoffDelaySeconds,
            HealthCheckRetryDelaySeconds = healthCheckRetryDelaySeconds
        };
    }

    /// <summary>Política só com health-retry: erros transitórios aguardam e são reentregues; sem backoff nem DLQ.</summary>
    public static ConsumerRetryOptions HealthOnly(
        int healthCheckRetryDelaySeconds = 300) {
        return new ConsumerRetryOptions {
            EnableHealthRetry = true,
            EnableRetryWithBackoff = false,
            EnableDeadLetter = false,
            HealthCheckRetryDelaySeconds = healthCheckRetryDelaySeconds
        };
    }

    /// <summary>Política com backoff e dead-letter, mas sem health-retry.</summary>
    public static ConsumerRetryOptions WithBackoff(
        int maxRetries = 10,
        int backoffDelaySeconds = 900) {
        return new ConsumerRetryOptions {
            EnableHealthRetry = false,
            EnableRetryWithBackoff = true,
            EnableDeadLetter = true,
            MaxRetries = maxRetries,
            BackoffDelaySeconds = backoffDelaySeconds
        };
    }
}