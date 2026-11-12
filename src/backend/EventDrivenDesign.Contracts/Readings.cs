namespace EventDrivenDesign.Contracts;

public sealed record MachineReading(
    string MachineId, string MachineType, string Shop, long Seq,
    DateTimeOffset DeviceTime, IReadOnlyDictionary<string, double> Signals);

public sealed record MachineAlarm(
    string MachineId, long AlarmSeq, long ReadingSeq, DateTimeOffset DeviceTime,
    AlarmKind Kind, string Severity, string? Signal, double? Value, double? Threshold);

public enum AlarmKind { Overheat, Vibration, Overload, EStop, LowBattery, OutOfBand, DataGap }
