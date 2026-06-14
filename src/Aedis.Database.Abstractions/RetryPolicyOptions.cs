namespace Aedis.Database.Abstractions;

public class RetryPolicyOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
    public double BackoffMultiplier { get; set; } = 2.0;

    public TimeSpan GetDelay(int attemptNumber) {
        var delay = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attemptNumber);
        var maxDelayMs = MaxDelay.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(Math.Min(delay, maxDelayMs));
    }
}