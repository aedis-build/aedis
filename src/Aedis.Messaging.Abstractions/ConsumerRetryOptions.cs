namespace Aedis.Messaging.Abstractions;

public class ConsumerRetryOptions
{
    public bool EnableHealthRetry { get; set; }
    public bool EnableRetryWithBackoff { get; set; }
    public bool EnableDeadLetter { get; set; }

    public int MaxRetries { get; set; } = 10;
    public int BackoffDelaySeconds { get; set; } = 900;
    public int HealthCheckRetryDelaySeconds { get; set; } = 300;

    public static ConsumerRetryOptions None() {
        return new ConsumerRetryOptions();
    }

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

    public static ConsumerRetryOptions HealthOnly(
        int healthCheckRetryDelaySeconds = 300) {
        return new ConsumerRetryOptions {
            EnableHealthRetry = true,
            EnableRetryWithBackoff = false,
            EnableDeadLetter = false,
            HealthCheckRetryDelaySeconds = healthCheckRetryDelaySeconds
        };
    }

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