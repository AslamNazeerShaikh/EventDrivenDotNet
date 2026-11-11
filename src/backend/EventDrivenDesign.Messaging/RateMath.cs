namespace EventDrivenDesign.Messaging;

/// <summary>
/// Splits a per-second event rate across the producer's 100 ms ticks,
/// spreading any remainder one extra event per tick over the first ticks.
/// </summary>
public static class RateMath
{
    /// <summary>Ticks per second for a 100 ms generator period.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>
    /// Computes how many events to emit on one tick so the ticks of a second sum to the rate.
    /// </summary>
    /// <param name="ratePerSecond">Commanded events per second. Must not be negative.</param>
    /// <param name="tickIndex">Zero-based tick within the second, from 0 to <see cref="TicksPerSecond"/> - 1.</param>
    /// <returns>Events to emit on this tick.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a negative rate or an out-of-range tick.</exception>
    public static int EventsForTick(int ratePerSecond, int tickIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ratePerSecond);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickIndex, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tickIndex, TicksPerSecond);
        return (ratePerSecond / TicksPerSecond) + (tickIndex < ratePerSecond % TicksPerSecond ? 1 : 0);
    }
}
