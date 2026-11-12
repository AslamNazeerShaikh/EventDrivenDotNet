using EventDrivenDesign.Domain;
using EventDrivenDesign.Persistence;
using Microsoft.Extensions.Hosting;

namespace EventDrivenDesign.Gateway;

/// <summary>
/// Ticks the collector once a second and commits each tick to the buffer. A failed write is not caught: it
/// faults the service, the host stops with a non-zero exit code, and Compose restarts the gateway from disk
/// (memory must never run ahead of the persisted sequence numbers).
/// </summary>
public sealed class CollectorService(
    Collector collector,
    GatewayBuffer buffer,
    IReadOnlyList<MachineInfo> machines,
    double faultsPerMinute,
    TimeProvider time
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (buffer.LoadPaused())
                continue;

            if (Random.Shared.NextDouble() < faultsPerMinute / 60)
            {
                var machine = machines[Random.Shared.Next(machines.Count)];
                var kinds = Signals
                    .For(machine.Type)
                    .Where(s => s.Kind is not null)
                    .Select(s => s.Kind!.Value)
                    .Distinct()
                    .ToArray();
                collector.InjectFault(machine.MachineId, kinds[Random.Shared.Next(kinds.Length)]);
            }

            await buffer.WriteTickAsync(collector.Tick(time.GetUtcNow()), CancellationToken.None);
        }
    }
}
