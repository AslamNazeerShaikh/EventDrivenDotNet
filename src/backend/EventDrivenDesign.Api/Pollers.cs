using System.Net.Http.Json;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Api;

/// <summary>
/// One loop per source (doc 00 §8). A failing source is logged and skipped: its last-success time stands still,
/// so its tile ages to Degraded and then Down instead of anything throwing into a request.
/// </summary>
public sealed class Pollers(
    Snapshot snapshot,
    HttpClient http,
    CentralQueries queries,
    KafkaWatcher plant,
    KafkaWatcher central,
    IReadOnlyList<string> gateways,
    IReadOnlyList<string> consumers,
    TimeProvider clock,
    ILogger<Pollers> log
) : BackgroundService
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SlowTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The shop code of a gateway endpoint: gateway-prs:8080 → PRS.</summary>
    public static string ShopOf(string endpoint) =>
        endpoint.Split(':')[0]["gateway-".Length..].ToUpperInvariant();

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        List<Task> loops =
        [
            Task.Factory.StartNew(
                () => plant.RunTail(ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ),
            Task.Factory.StartNew(
                () => central.RunTail(ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ),
            Loop("plant-offsets", TimeSpan.FromSeconds(2), ct, async c =>
            {
                var sample = await plant.PollOffsetsAsync().WaitAsync(SlowTimeout, c);
                snapshot.SetPlantEnd(sample.End.Values.Sum(), clock.GetUtcNow());
            }),
            Loop("central-offsets", TimeSpan.FromSeconds(2), ct, async c =>
                snapshot.SetCentral(
                    await central.PollOffsetsAsync().WaitAsync(SlowTimeout, c),
                    clock.GetUtcNow()
                )
            ),
            Loop("database", TimeSpan.FromSeconds(2), ct, RefreshDbAsync),
        ];
        loops.AddRange(
            gateways.Select(g =>
                Loop(g, TimeSpan.FromSeconds(1), ct, async c =>
                    snapshot.SetGateway(
                        ShopOf(g),
                        await Metrics<GatewayMetrics>(g, c),
                        clock.GetUtcNow()
                    )
                )
            )
        );
        loops.AddRange(
            consumers.Select(n =>
                Loop(n, TimeSpan.FromSeconds(1), ct, async c =>
                    snapshot.SetConsumer(
                        n.Split(':')[0],
                        await Metrics<ConsumerMetrics>(n, c),
                        clock.GetUtcNow()
                    )
                )
            )
        );
        return Task.WhenAll(loops);
    }

    /// <summary>Also called by the reconciliation endpoint, so its verdict rests on progress read after the offsets.</summary>
    public async Task RefreshDbAsync(CancellationToken ct)
    {
        var startedAt = clock.GetUtcNow(); // the sample is as old as its query's start: rows inserted meanwhile are not in it
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SlowTimeout);
        var progress = await queries.ProgressAsync(cts.Token);
        var (received, inserted) = await queries.TotalsAsync("readings", cts.Token);
        snapshot.SetDb(new DbSample(progress, received, inserted), startedAt);
    }

    private async Task<T> Metrics<T>(string endpoint, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HttpTimeout);
        return (
            await http.GetFromJsonAsync<T>($"http://{endpoint}/metrics", Json.Options, cts.Token)
        )!;
    }

    private Task Loop(
        string name,
        TimeSpan every,
        CancellationToken ct,
        Func<CancellationToken, Task> poll
    ) =>
        Task.Run(
            async () =>
            {
                using var timer = new PeriodicTimer(every, clock);
                string? lastReason = null;
                do
                {
                    try
                    {
                        await poll(ct);
                        lastReason = null;
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        // Log a new reason only: a source that stays down must not flood the log.
                        var reason = $"{e.GetType().Name}: {e.Message}";
                        if (reason != lastReason)
                            log.LogWarning("Poll of {Source} failed: {Reason}", name, reason);
                        lastReason = reason;
                    }
                } while (await TimerTick(timer, ct));
            },
            ct
        );

    private static async Task<bool> TimerTick(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
