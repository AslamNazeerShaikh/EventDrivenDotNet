using EventDrivenDesign.Domain;
using EventDrivenDesign.Persistence;
using Microsoft.Extensions.Hosting;

namespace EventDrivenDesign.Gateway;

/// <summary>
/// Ticks the collector once a second and commits each tick to the buffer. Before the first timer wait it
/// backfills the seconds missed while it was down (doc 02 §4.3): unless paused, it writes every batch of
/// <see cref="Collector.Backfill"/>, repeating with a fresh <c>now</c> until a pass yields nothing, so the
/// seconds spent backfilling are covered too. A failed write is not caught: it
/// faults the service, the host stops with a non-zero exit code, and Compose restarts the gateway from disk
/// (memory must never run ahead of the persisted sequence numbers).
/// </summary>
public sealed class CollectorService(
    Collector collector,
    GatewayBuffer buffer,
    IReadOnlyList<MachineInfo> machines,
    double faultsPerMinute,
    int historyDepthSeconds,
    TimeProvider time
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!buffer.LoadPaused())
        {
            while (true)
            {
                var any = false;
                foreach (var batch in collector.Backfill(time.GetUtcNow(), historyDepthSeconds))
                {
                    any = true;
                    await buffer
                        .WriteTickAsync(batch, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                if (!any)
                    break;
            }
        }

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
