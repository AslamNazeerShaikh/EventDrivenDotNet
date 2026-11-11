using System.Diagnostics;

namespace EventDrivenDesign.Infrastructure;

/// <summary>
/// Samples process CPU usage for heartbeats using the spec section 8 formula:
/// CPU % = delta TotalProcessorTime / (delta wall time * Environment.ProcessorCount) * 100.
/// </summary>
public sealed class ResourceSampler
{
    private TimeSpan _lastCpu;
    private long _lastAtMs;
    private bool _hasSample;

    /// <summary>
    /// Computes CPU percent between two samples. Pure for testability.
    /// </summary>
    /// <param name="cpuBefore">Total processor time at the previous sample.</param>
    /// <param name="cpuAfter">Total processor time at the current sample.</param>
    /// <param name="wallElapsed">Wall-clock time between the samples.</param>
    /// <param name="processorCount">Value of <see cref="Environment.ProcessorCount"/>.</param>
    /// <returns>CPU percent, or 0 when the wall interval is not positive.</returns>
    public static double ComputeCpuPercent(TimeSpan cpuBefore, TimeSpan cpuAfter, TimeSpan wallElapsed, int processorCount)
    {
        if (wallElapsed <= TimeSpan.Zero || processorCount <= 0)
        {
            return 0.0;
        }

        return Math.Max(0.0, (cpuAfter - cpuBefore).TotalMilliseconds / (wallElapsed.TotalMilliseconds * processorCount) * 100.0);
    }

    /// <summary>
    /// Takes one CPU sample for the current process and returns the percent since the last call.
    /// </summary>
    /// <returns>CPU percent since the previous call, or 0 on the first call.</returns>
    public double Sample()
    {
        using Process process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long nowMs = Environment.TickCount64;
        double result = _hasSample
            ? ComputeCpuPercent(_lastCpu, cpu, TimeSpan.FromMilliseconds(nowMs - _lastAtMs), Environment.ProcessorCount)
            : 0.0;
        _lastCpu = cpu;
        _lastAtMs = nowMs;
        _hasSample = true;
        return result;
    }
}
