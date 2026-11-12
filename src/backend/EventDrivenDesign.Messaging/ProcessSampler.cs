using System.Diagnostics;
using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Messaging;

/// <summary>CPU % is the TotalProcessorTime delta over wall time since the previous call (100 = one full core).</summary>
public sealed class ProcessSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastTicks;

    public ProcessSampler()
    {
        _process.Refresh();
        _lastCpu = _process.TotalProcessorTime;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public ProcessStats Sample()
    {
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var now = Stopwatch.GetTimestamp();
        var wall = Stopwatch.GetElapsedTime(_lastTicks, now);
        var percent = wall > TimeSpan.Zero ? (cpu - _lastCpu) / wall * 100 : 0;
        (_lastCpu, _lastTicks) = (cpu, now);
        return new ProcessStats(percent, Environment.WorkingSet, GC.GetTotalMemory(false));
    }
}
