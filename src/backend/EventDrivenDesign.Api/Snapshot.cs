using System.Collections.Concurrent;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;

namespace EventDrivenDesign.Api;

/// <summary>A value and the moment its poll last succeeded. Health is derived from that age (doc 00 §8).</summary>
public sealed record Stamped<T>(T Value, DateTimeOffset At);

/// <summary>Per-partition latest offsets and the committed offsets of the writers' group.</summary>
public sealed record OffsetSample(IReadOnlyDictionary<int, long> End, IReadOnlyDictionary<int, long> Committed)
{
    public long Lag => End.Sum(e => Math.Max(0, e.Value - Committed.GetValueOrDefault(e.Key)));
}

public sealed record DbSample(IReadOnlyList<ProgressRow> Progress, long Received, long Inserted);

/// <summary>
/// The latest value from every source, each with its last-success time. Pollers write, requests read:
/// every slot is replaced whole, so a reader never sees a half-written value and never waits on a source.
/// </summary>
public sealed class Snapshot(IEnumerable<string> shops, IEnumerable<string> consumers)
{
    private readonly ConcurrentDictionary<string, Stamped<GatewayMetrics>> _gateways = new();
    private readonly ConcurrentDictionary<string, Stamped<ConsumerMetrics>> _consumers = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _settledSince = new();
    private Stamped<long>? _plantEnd;
    private Stamped<OffsetSample>? _central;
    private Stamped<DbSample>? _db;
    private long _centralStableTicks; // 0: no run
    private long _plantNewestTicks;
    private long _centralNewestTicks;

    public IReadOnlyList<string> Shops { get; } = [.. shops];

    public IReadOnlyList<string> Consumers { get; } = [.. consumers];

    public Stamped<GatewayMetrics>? Gateway(string shop) => _gateways.GetValueOrDefault(shop);

    public Stamped<ConsumerMetrics>? Consumer(string id) => _consumers.GetValueOrDefault(id);

    public Stamped<long>? PlantEnd => Volatile.Read(ref _plantEnd);

    public Stamped<OffsetSample>? Central => Volatile.Read(ref _central);

    /// <summary>First sample of the current run of identical end offsets with nothing uncommitted: a DB sample started after it holds every record up to those offsets.</summary>
    public DateTimeOffset? CentralStableSince => Newest(ref _centralStableTicks);

    public Stamped<DbSample>? Db => Volatile.Read(ref _db);

    public DateTimeOffset? PlantNewest => Newest(ref _plantNewestTicks);

    public DateTimeOffset? CentralNewest => Newest(ref _centralNewestTicks);

    /// <summary>When this gateway was first seen paused with nothing buffered, in its current such streak.</summary>
    public DateTimeOffset? SettledSince(string shop) => _settledSince.TryGetValue(shop, out var t) ? t : null;

    public void SetGateway(string shop, GatewayMetrics m, DateTimeOffset at)
    {
        _gateways[shop] = new(m, at);
        if (m.Paused && m.BufferedReadings + m.BufferedAlarms == 0) _settledSince.TryAdd(shop, at);
        else _settledSince.TryRemove(shop, out _);
    }

    public void SetConsumer(string id, ConsumerMetrics m, DateTimeOffset at) => _consumers[id] = new(m, at);

    public void SetPlantEnd(long total, DateTimeOffset at) => Volatile.Write(ref _plantEnd, new(total, at));

    // One writer (the offsets loop). The run is written first: a reader that sees the new run with the old sample can only wait.
    public void SetCentral(OffsetSample sample, DateTimeOffset at)
    {
        var same = _central?.Value is { } last && last.Lag == 0 && last.End.Count == sample.End.Count
            && last.End.All(e => sample.End.TryGetValue(e.Key, out var v) && v == e.Value);
        Volatile.Write(ref _centralStableTicks, sample.Lag != 0 ? 0 : same && _centralStableTicks != 0 ? _centralStableTicks : at.UtcTicks);
        Volatile.Write(ref _central, new(sample, at));
    }

    /// <summary><paramref name="startedAt"/> is when the query began. A result that began earlier than the stored one never replaces it.</summary>
    public void SetDb(DbSample sample, DateTimeOffset startedAt)
    {
        var next = new Stamped<DbSample>(sample, startedAt);
        Stamped<DbSample>? seen;
        while ((seen = Volatile.Read(ref _db)) is null || seen.At <= startedAt)
            if (Interlocked.CompareExchange(ref _db, next, seen) == seen) return;
    }

    // The tails call these once per record: a lock-free monotonic max.
    public void SeenPlant(DateTimeOffset t) => Max(ref _plantNewestTicks, t.UtcTicks);

    public void SeenCentral(DateTimeOffset t) => Max(ref _centralNewestTicks, t.UtcTicks);

    private static DateTimeOffset? Newest(ref long ticks) =>
        Volatile.Read(ref ticks) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    private static void Max(ref long slot, long value)
    {
        long seen;
        while ((seen = Volatile.Read(ref slot)) < value && Interlocked.CompareExchange(ref slot, value, seen) != seen) { }
    }
}
