using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Domain;

public sealed record AlarmEvent(AlarmKind Kind, string Severity, string? Signal, double? Value, double? Threshold);

public sealed record SimTick(IReadOnlyDictionary<string, double> Signals, IReadOnlyList<AlarmEvent> Alarms);

public sealed class MachineSimulator(MachineInfo machine, int seed)
{
    private const int FaultTicks = 10;

    private sealed class Fault(double value) { public double Value = value; public int Remaining = FaultTicks; public bool Alarmed; }

    private readonly Random _rng = new(seed);
    private readonly IReadOnlyList<SignalSpec> _specs = Signals.For(machine.Type);
    private readonly double[] _x = Signals.For(machine.Type).Select(s => (s.Min + s.Max) / 2).ToArray();
    private readonly Dictionary<string, Fault> _faults = [];

    public SimTick Next()
    {
        var values = new Dictionary<string, double>(_specs.Count);
        var alarms = new List<AlarmEvent>();
        for (var i = 0; i < _specs.Count; i++)
        {
            var s = _specs[i];
            var mid = (s.Min + s.Max) / 2;
            _x[i] = Math.Clamp(_x[i] + 0.1 * (mid - _x[i]) + (s.Max - s.Min) / 20 * Gaussian(), s.Min, s.Max);
            values[s.Name] = _x[i];

            if (!_faults.TryGetValue(s.Name, out var f)) continue;
            values[s.Name] = f.Value;
            if (!f.Alarmed)
            {
                f.Alarmed = true;
                alarms.Add(new AlarmEvent(s.Kind!.Value, Severity(s.Kind.Value), s.Name, f.Value, s.Above ?? s.Below));
            }
            if (--f.Remaining == 0) _faults.Remove(s.Name);
        }
        return new SimTick(values, alarms);
    }

    public bool InjectFault(AlarmKind kind)
    {
        var s = _specs.FirstOrDefault(x => x.Kind == kind);
        if (s is null) return false;
        var margin = Math.Max(0.1 * (s.Max - s.Min), 0.5);
        _faults[s.Name] = new Fault(s.Above is { } a ? a + margin : s.Below!.Value - margin);
        return true;
    }

    private static string Severity(AlarmKind kind) => kind switch
    {
        AlarmKind.EStop or AlarmKind.Overheat => "critical",
        AlarmKind.Overload => "high",
        _ => "warning",
    };

    // Box-Muller; 1 - NextDouble() is in (0, 1], so the log is finite.
    private double Gaussian() =>
        Math.Sqrt(-2 * Math.Log(1 - _rng.NextDouble())) * Math.Cos(2 * Math.PI * _rng.NextDouble());
}
