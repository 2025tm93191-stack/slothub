namespace Notification.Domain.Messages;

/// <summary>Exponential backoff for failed deliveries: base × 2^(attempt-1), capped.</summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)
{
    public static RetryPolicy Default { get; } = new(5, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

    public TimeSpan DelayAfter(int attempt)
    {
        var factor = Math.Pow(2, Math.Max(0, attempt - 1));
        var delay = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * factor);
        return delay < MaxDelay ? delay : MaxDelay;
    }
}
