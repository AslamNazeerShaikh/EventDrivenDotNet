using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Domain;

public sealed record SignalSpec(string Name, double Min, double Max, AlarmKind? Kind = null, double? Above = null, double? Below = null);

public static class Signals
{
    private static readonly Dictionary<MachineType, SignalSpec[]> Table = new()
    {
        [MachineType.StampingPress] =
        [
            new("tonnage_t", 600, 800, AlarmKind.Overload, Above: 900),
            new("strokes_per_min", 16, 20),
            new("die_temp_c", 40, 60, AlarmKind.Overheat, Above: 80),
        ],
        [MachineType.WeldRobot] =
        [
            new("weld_current_ka", 8, 15, AlarmKind.OutOfBand, Above: 16, Below: 7),
            new("weld_time_ms", 100, 330),
            new("motor_temp_c", 40, 70, AlarmKind.Overheat, Above: 85),
        ],
        [MachineType.PaintBooth] =
        [
            new("air_temp_c", 21, 24, AlarmKind.OutOfBand, Above: 25, Below: 20),
            new("humidity_pct", 40, 60, AlarmKind.OutOfBand, Above: 65, Below: 35),
            new("airflow_m_s", 0.3, 0.5),
        ],
        [MachineType.Cnc] =
        [
            new("spindle_rpm", 6000, 12000),
            new("vibration_mm_s", 0.5, 2.8, AlarmKind.Vibration, Above: 4.5),
            new("load_pct", 30, 80, AlarmKind.Overload, Above: 95),
        ],
        [MachineType.Conveyor] =
        [
            new("speed_m_min", 8, 12),
            new("motor_current_a", 10, 20, AlarmKind.Overload, Above: 25),
            new("vibration_mm_s", 0.5, 2.8, AlarmKind.Vibration, Above: 4.5),
        ],
        [MachineType.Agv] =
        [
            new("battery_pct", 20, 100, AlarmKind.LowBattery, Below: 15),
            new("velocity_m_s", 0, 2),
            new("e_stop", 0, 0, AlarmKind.EStop, Above: 0.5),
        ],
    };

    public static IReadOnlyList<SignalSpec> For(MachineType type) => Table[type];
}
