namespace EventDrivenDesign.Consumer;

/// <summary>Drain cap for the readings loop (doc 00 §7): a batch of N rows may not finish sooner than N / rate seconds. 0 means uncapped.</summary>
public sealed class RateCap(int rowsPerSecond)
{
    public TimeSpan DelayAfter(int rows, TimeSpan elapsed)
    {
        if (rowsPerSecond <= 0)
            return TimeSpan.Zero;
        var wait = TimeSpan.FromSeconds((double)rows / rowsPerSecond) - elapsed;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}

/// <summary>Events per second over the last 10 s (same window as the gateway sender).</summary>
public sealed class RateMeter(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private readonly Queue<(DateTimeOffset At, int Count)> _recent = new();

    public void Add(int count)
    {
        lock (_recent)
            _recent.Enqueue((time.GetUtcNow(), count));
    }

    public double PerSecond
    {
        get
        {
            lock (_recent)
            {
                var cutoff = time.GetUtcNow() - Window;
                while (_recent.Count > 0 && _recent.Peek().At <= cutoff)
                    _recent.Dequeue();
                return _recent.Sum(r => r.Count) / Window.TotalSeconds;
            }
        }
    }
}
