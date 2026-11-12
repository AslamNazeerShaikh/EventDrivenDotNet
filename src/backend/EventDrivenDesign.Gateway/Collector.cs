using System.Collections.Concurrent;
using System.Text.Json;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Gateway;

/// <summary>
/// Reads every simulated machine once per tick and numbers the results from the persisted state
/// (doc 02 §4.2 rule 2). Tick runs on one timer thread; InjectFault may be called from any thread.
/// </summary>
public sealed class Collector
{
    private sealed record Slot(
        MachineInfo Info,
        MachineSimulator Sim,
        IReadOnlyList<SignalSpec> Specs,
        long[] Seqs
    )
    {
        public long? LastDeviceMs { get; set; }
    }

    private readonly Slot[] _slots;
    private readonly Dictionary<string, Slot> _byId;
    private readonly ConcurrentQueue<(Slot Slot, AlarmKind Kind)> _faults = new();
    private volatile IReadOnlyDictionary<string, long> _lastSeq;

    public Collector(
        string plant,
        string shop,
        int seed,
        IReadOnlyDictionary<string, MachineState> resume
    )
    {
        _slots = MachineCatalog
            .ForShop(plant, shop)
            .Select(m =>
            {
                resume.TryGetValue(m.MachineId, out var s);
                return new Slot(
                    m,
                    new MachineSimulator(m, seed + m.Index),
                    Signals.For(m.Type),
                    [s?.LastSeq ?? 0, s?.LastAlarmSeq ?? 0]
                )
                {
                    LastDeviceMs = s?.LastDeviceTimeMs,
                };
            })
            .ToArray();
        _byId = _slots.ToDictionary(s => s.Info.MachineId);
        _lastSeq = Snapshot();
    }

    public IReadOnlyDictionary<string, long> LastSeqByMachine => _lastSeq;

    public long Generated => _lastSeq.Values.Sum();

    /// <summary>
    /// Sets numbering and last device time from the given state; machines missing from it keep theirs.
    /// Call it only while no CollectorService runs.
    /// </summary>
    public void Resume(IReadOnlyDictionary<string, MachineState> state)
    {
        foreach (var slot in _slots)
        {
            if (!state.TryGetValue(slot.Info.MachineId, out var s))
                continue;
            slot.Seqs[0] = s.LastSeq;
            slot.Seqs[1] = s.LastAlarmSeq;
            slot.LastDeviceMs = s.LastDeviceTimeMs;
        }
        _lastSeq = Snapshot();
    }

    public bool InjectFault(string machineId, AlarmKind kind)
    {
        if (!_byId.TryGetValue(machineId, out var slot) || !slot.Specs.Any(s => s.Kind == kind))
            return false;
        _faults.Enqueue((slot, kind));
        return true;
    }

    public TickBatch Tick(DateTimeOffset tickTime)
    {
        while (_faults.TryDequeue(out var f))
            f.Slot.Sim.InjectFault(f.Kind);

        var rows = new List<OutboxRow>(_slots.Length);
        var states = new List<MachineState>(_slots.Length);
        foreach (var slot in _slots)
            states.Add(BuildTick(rows, slot, tickTime));
        _lastSeq = Snapshot();
        return new TickBatch(rows, states);
    }

    /// <summary>
    /// Regenerates the whole seconds missed since each numbered machine's last device time, with their
    /// original device times, up to <paramref name="depthSeconds"/> seconds. Anything older becomes one
    /// <see cref="AlarmKind.DataGap"/> alarm per machine. Yields one batch per 60 seconds of device time,
    /// oldest first. Queued faults stay queued: they reach the next live tick, never a backfilled one.
    /// </summary>
    public IEnumerable<TickBatch> Backfill(DateTimeOffset now, int depthSeconds)
    {
        var plans = new List<(Slot Slot, DateTimeOffset LastTick, long N, long From)>();
        foreach (var slot in _slots)
        {
            if (slot.LastDeviceMs is not { } lastMs)
                continue;
            var m = slot.Info;
            var lastTick = DateTimeOffset
                .FromUnixTimeMilliseconds(lastMs)
                .AddMilliseconds(-m.Index * 10);
            var n = (long)Math.Floor((now - lastTick).TotalSeconds);
            if (n <= 0)
                continue;
            plans.Add((slot, lastTick, n, n - Math.Min(n, depthSeconds) + 1));
        }

        var batches = new List<TickBatch>();
        if (plans.Count == 0)
        {
            _lastSeq = Snapshot();
            return batches;
        }

        var gapRows = new List<OutboxRow>();
        var gapStates = new Dictionary<string, MachineState>(StringComparer.Ordinal);
        foreach (var (slot, lastTick, n, from) in plans)
        {
            if (n <= depthSeconds)
                continue;
            var m = slot.Info;
            var alarmSeq = ++slot.Seqs[1];
            var time = lastTick.AddSeconds(1).AddMilliseconds(m.Index * 10); // the first missing tick
            var ms = time.ToUnixTimeMilliseconds();
            gapRows.Add(
                new OutboxRow(
                    0,
                    Lane.Alarm,
                    m.MachineId,
                    alarmSeq,
                    ms,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new MachineAlarm(
                            m.MachineId,
                            alarmSeq,
                            slot.Seqs[0],
                            time,
                            AlarmKind.DataGap,
                            "warning",
                            null,
                            n - depthSeconds,
                            depthSeconds
                        ),
                        Json.Options
                    )
                )
            );
            gapStates[m.MachineId] = new MachineState(
                m.MachineId,
                slot.Seqs[0],
                slot.Seqs[1],
                slot.LastDeviceMs!.Value
            );
        }

        var oldest = plans.Min(p => p.LastTick + TimeSpan.FromSeconds(p.From));
        var maxEnd = plans.Max(p => p.LastTick + TimeSpan.FromSeconds(p.N));
        for (var b = 0L; ; b++)
        {
            var ws = oldest + TimeSpan.FromSeconds(60 * b);
            if (ws > maxEnd)
                break;
            var we = ws + TimeSpan.FromSeconds(60);
            var rows = new List<OutboxRow>();
            if (b == 0)
                rows.AddRange(gapRows);
            var states = new Dictionary<string, MachineState>(StringComparer.Ordinal);
            foreach (var (slot, lastTick, n, from) in plans)
            {
                var kLo = Math.Max(from, (long)Math.Ceiling((ws - lastTick).TotalSeconds));
                var kHi = Math.Min(n, (long)Math.Ceiling((we - lastTick).TotalSeconds) - 1);
                for (var k = kLo; k <= kHi; k++)
                {
                    var tick = lastTick + TimeSpan.FromSeconds(k);
                    var state = BuildTick(rows, slot, tick);
                    states[slot.Info.MachineId] = state;
                }
            }
            if (b == 0)
                foreach (var (id, state) in gapStates)
                    states.TryAdd(id, state);
            if (rows.Count > 0)
                batches.Add(new TickBatch(rows, [.. states.Values]));
        }

        if (batches.Count == 0 && gapRows.Count > 0) // depth 0: everything is a gap
            batches.Add(new TickBatch(gapRows, [.. gapStates.Values]));
        _lastSeq = Snapshot();
        return batches;
    }

    private MachineState BuildTick(List<OutboxRow> rows, Slot slot, DateTimeOffset tickTime)
    {
        var m = slot.Info;
        var time = tickTime.AddMilliseconds(m.Index * 10);
        var ms = time.ToUnixTimeMilliseconds();
        var sim = slot.Sim.Next();
        var seq = ++slot.Seqs[0];

        rows.Add(
            new OutboxRow(
                0,
                Lane.Reading,
                m.MachineId,
                seq,
                ms,
                JsonSerializer.SerializeToUtf8Bytes(
                    new MachineReading(
                        m.MachineId,
                        m.Type.ToString(),
                        m.Shop,
                        seq,
                        time,
                        sim.Signals
                    ),
                    Json.Options
                )
            )
        );
        foreach (var a in sim.Alarms)
        {
            var alarmSeq = ++slot.Seqs[1];
            rows.Add(
                new OutboxRow(
                    0,
                    Lane.Alarm,
                    m.MachineId,
                    alarmSeq,
                    ms,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new MachineAlarm(
                            m.MachineId,
                            alarmSeq,
                            seq,
                            time,
                            a.Kind,
                            a.Severity,
                            a.Signal,
                            a.Value,
                            a.Threshold
                        ),
                        Json.Options
                    )
                )
            );
        }
        slot.LastDeviceMs = ms;
        return new MachineState(m.MachineId, seq, slot.Seqs[1], ms);
    }

    private Dictionary<string, long> Snapshot() =>
        _slots.ToDictionary(s => s.Info.MachineId, s => s.Seqs[0]);
}
