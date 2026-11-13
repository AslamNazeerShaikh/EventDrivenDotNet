using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Consumer;

/// <summary>
/// Well-formed JSON can still lack required fields, and the contracts are plain records. Such a record would fail the insert
/// forever and wedge its partition, so it is quarantined as malformed instead (doc 02 §4.10 row 9).
/// </summary>
public static class RecordValidation
{
    public static string? Problem(object record) =>
        record switch
        {
            MachineReading r => Text(r.MachineId, "machineId")
                ?? Text(r.MachineType, "machineType")
                ?? Text(r.Shop, "shop")
                ?? (r.Seq <= 0 ? "seq must be positive" : null)
                ?? (r.Signals is null ? "signals is required" : null),
            MachineAlarm a => Text(a.MachineId, "machineId")
                ?? Text(a.Severity, "severity")
                ?? (a.AlarmSeq <= 0 ? "alarmSeq must be positive" : null),
            _ => null,
        };

    private static string? Text(string? value, string name) =>
        string.IsNullOrEmpty(value) ? $"{name} is required"
        : value.Contains('\0') ? $"{name} contains a NUL character"
        : null;
}
