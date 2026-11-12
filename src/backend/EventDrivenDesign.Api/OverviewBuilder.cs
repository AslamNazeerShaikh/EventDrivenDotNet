using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;

namespace EventDrivenDesign.Api;

/// <summary>Turns the latest snapshot into the dashboard's models. Pure: a source that never answered is Down, never an exception.</summary>
public static class OverviewBuilder
{
    // Offsets must be sampled this long after the last gateway went quiet, so records still in the mirror have landed.
    private static readonly TimeSpan MirrorSettle = TimeSpan.FromSeconds(2);

    public static Overview Build(Snapshot s, DateTimeOffset now)
    {
        var gateways = s.Shops.Select(shop => GatewayTile(shop, s.Gateway(shop), now)).ToList();
        var consumers = s.Consumers.Select(id => ConsumerTile(id, s.Consumer(id), now)).ToList();
        var lag = ConsumerLag(s);
        var live = gateways.Where(g => g.Health is not Down && !g.Paused).Sum(g => g.SentPerSecond);
        var drain = consumers.Where(c => c.Health is not Down).Sum(c => c.InsertedPerSecond);
        var db = s.Db;
        var stored = db?.Value.Progress.Sum(p => p.Stored) ?? 0;
        double? freshness = db is { Value.Progress: { Count: > 0 } rows }
            ? Math.Max(0, (now - rows.Max(p => p.MaxDeviceTime)).TotalSeconds)
            : null;

        return new Overview(now, gateways,
            new ClusterTile(Health(s.PlantEnd?.At, now), s.PlantEnd?.Value ?? 0, s.PlantNewest), MirrorLag(s),
            new ClusterTile(Health(s.Central?.At, now), s.Central?.Value.End.Values.Sum() ?? 0, s.CentralNewest),
            lag, consumers, new DatabaseTile(Health(db?.At, now), stored, freshness),
            live, drain, 
            // Unknown, not "0": with central dark or every writer down the last lag is stale and the backlog is growing.
            s.Central is null || Health(s.Central.At, now) is not Healthy || consumers.All(c => c.Health is Down) ? null : Proof.DrainEtaSeconds(lag, drain, live));
    }

    public static Reconciliation Reconcile(Snapshot s, DateTimeOffset now, long? duplicateRows)
    {
        var generated = new Dictionary<string, long>();
        foreach (var shop in s.Shops)
            if (s.Gateway(shop) is { } g)
                foreach (var (machine, seq) in g.Value.LastSeqByMachine)
                    generated[machine] = Math.Max(generated.GetValueOrDefault(machine), seq);

        var gateways = s.Shops.Select(shop => s.Gateway(shop)).ToList();
        var fresh = gateways.All(g => g is not null && Health(g.At, now) is Healthy);
        var allPaused = fresh && gateways.All(g => g!.Value.Paused);
        var drained = fresh && IsDrained(s, gateways!, now);
        var db = s.Db?.Value;

        return Proof.Reconcile(generated, db?.Progress ?? [], db is null ? 0 : db.Received - db.Inserted, duplicateRows, allPaused, drained);
    }

    public static Chart BuildChart(IReadOnlyList<(DateTimeOffset Bucket, long Readings)> buckets, DateTimeOffset? watermark) =>
        new(watermark, [.. buckets.Select(b => new ChartPoint(b.Bucket, b.Readings, watermark is null || b.Bucket > watermark))]);

    private static bool IsDrained(Snapshot s, List<Stamped<GatewayMetrics>> gateways, DateTimeOffset now)
    {
        if (s.Central is not { } central || s.PlantEnd is not { } plant || s.Db is not { } db) return false;
        if (Health(central.At, now) is not Healthy || Health(plant.At, now) is not Healthy || Health(db.At, now) is not Healthy) return false;
        if (gateways.Any(g => g.Value.BufferedReadings + g.Value.BufferedAlarms != 0)) return false;
        // The lag must be measured MirrorSettle after the last gateway went quiet, and the progress rows after the offsets were last
        // known to be final. The run start, not the newest sample: an offsets poll finishing mid-query must not make an old DB sample stale.
        var quiet = s.Shops.Select(shop => s.SettledSince(shop)).ToList();
        if (quiet.Any(q => q is null) || central.At < quiet.Max()!.Value + MirrorSettle || s.CentralStableSince is not { } stable || db.At < stable) return false;
        // Zero, not the 2 s tolerance of a live view: a record still in the mirror would otherwise read as a loss.
        return ConsumerLag(s) == 0 && (s.PlantNewest is null || MirrorLag(s) is <= 0);
    }

    private static long ConsumerLag(Snapshot s) => s.Central?.Value.Lag ?? 0;

    // Both tails empty means nothing was produced since the API started: no lag, not an unknown one.
    private static double? MirrorLag(Snapshot s) => (s.PlantNewest, s.CentralNewest) switch
    {
        (null, _) => s.CentralNewest is null ? null : 0,
        (_, null) => null,
        var (plant, central) => Math.Max(0, (plant.Value - central.Value).TotalSeconds),
    };

    private static ServiceHealth Health(DateTimeOffset? lastSuccess, DateTimeOffset now) =>
        lastSuccess is { } at ? Proof.HealthFromAge(now - at, at) : new Down(DateTimeOffset.UnixEpoch);

    private static GatewayTile GatewayTile(string shop, Stamped<GatewayMetrics>? g, DateTimeOffset now) => g is null
        ? new(shop, Health(null, now), "Unknown", false, 0, 0, 0, 0, 0)
        : new(shop, Health(g.At, now), g.Value.State, g.Value.Paused, g.Value.SentPerSecond,
              g.Value.BufferedReadings, g.Value.BufferedAlarms, g.Value.OldestUnsentAgeMs, g.Value.Generated);

    private static ConsumerTile ConsumerTile(string id, Stamped<ConsumerMetrics>? c, DateTimeOffset now) => c is null
        ? new(id, Health(null, now), 0, 0, 0)
        : new(id, Health(c.At, now), c.Value.InsertedPerSecond, c.Value.Received, c.Value.Inserted);
}
