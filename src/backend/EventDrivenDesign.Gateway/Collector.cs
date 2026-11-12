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
    private sealed record Slot(MachineInfo Info, MachineSimulator Sim, IReadOnlyList<SignalSpec> Specs, long[] Seqs);

    private readonly Slot[] _slots;
    private readonly Dictionary<string, Slot> _byId;
    private readonly ConcurrentQueue<(Slot Slot, AlarmKind Kind)> _faults = new();
    private volatile IReadOnlyDictionary<string, long> _lastSeq;

    public Collector(string plant, string shop, int seed, IReadOnlyDictionary<string, MachineState> resume)
    {
        _slots = MachineCatalog.ForShop(plant, shop).Select(m =>
        {
            resume.TryGetValue(m.MachineId, out var s);
            return new Slot(m, new MachineSimulator(m, seed + m.Index), Signals.For(m.Type), [s?.LastSeq ?? 0, s?.LastAlarmSeq ?? 0]);
        }).ToArray();
        _byId = _slots.ToDictionary(s => s.Info.MachineId);
        _lastSeq = Snapshot();
    }

    public IReadOnlyDictionary<string, long> LastSeqByMachine => _lastSeq;

    public long Generated => _lastSeq.Values.Sum();

    public bool InjectFault(string machineId, AlarmKind kind)
    {
        if (!_byId.TryGetValue(machineId, out var slot) || !slot.Specs.Any(s => s.Kind == kind)) return false;
        _faults.Enqueue((slot, kind));
        return true;
    }

    public TickBatch Tick(DateTimeOffset tickTime)
    {
        while (_faults.TryDequeue(out var f)) f.Slot.Sim.InjectFault(f.Kind);

        var rows = new List<OutboxRow>(_slots.Length);
        var states = new List<MachineState>(_slots.Length);
        foreach (var slot in _slots)
        {
            var m = slot.Info;
            var time = tickTime.AddMilliseconds(m.Index * 10);
            var ms = time.ToUnixTimeMilliseconds();
            var sim = slot.Sim.Next();
            var seq = ++slot.Seqs[0];

            rows.Add(new OutboxRow(0, Lane.Reading, m.MachineId, seq, ms,
                JsonSerializer.SerializeToUtf8Bytes(new MachineReading(m.MachineId, m.Type.ToString(), m.Shop, seq, time, sim.Signals), Json.Options)));
            foreach (var a in sim.Alarms)
            {
                var alarmSeq = ++slot.Seqs[1];
                rows.Add(new OutboxRow(0, Lane.Alarm, m.MachineId, alarmSeq, ms,
                    JsonSerializer.SerializeToUtf8Bytes(new MachineAlarm(m.MachineId, alarmSeq, seq, time, a.Kind, a.Severity, a.Signal, a.Value, a.Threshold), Json.Options)));
            }
            states.Add(new MachineState(m.MachineId, seq, slot.Seqs[1], ms));
        }
        _lastSeq = Snapshot();
        return new TickBatch(rows, states);
    }

    private Dictionary<string, long> Snapshot() => _slots.ToDictionary(s => s.Info.MachineId, s => s.Seqs[0]);
}
