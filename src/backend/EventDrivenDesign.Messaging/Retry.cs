namespace EventDrivenDesign.Messaging;

public static class Retry
{
    /// <summary>Full-jitter exponential backoff: uniform in [0, min(30 s, 0.1 s * 2^attempt)].</summary>
    public static TimeSpan Backoff(int attempt, Random rng)
    {
        var capSeconds = Math.Min(30.0, 0.1 * Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(rng.NextDouble() * capSeconds);
    }
}
