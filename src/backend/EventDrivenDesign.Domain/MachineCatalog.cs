namespace EventDrivenDesign.Domain;

public enum MachineType { StampingPress, WeldRobot, PaintBooth, Cnc, Conveyor, Agv }

public sealed record MachineInfo(string MachineId, MachineType Type, string Shop, int Index);

public static class MachineCatalog
{
    private static readonly (string Shop, (MachineType Type, char Letter, int Count)[] Mix)[] Plan =
    [
        ("PRS", [(MachineType.StampingPress, 'S', 30), (MachineType.Conveyor, 'V', 50), (MachineType.Agv, 'A', 20)]),
        ("BDY", [(MachineType.WeldRobot, 'W', 80), (MachineType.Conveyor, 'V', 10), (MachineType.Agv, 'A', 10)]),
        ("PNT", [(MachineType.PaintBooth, 'B', 40), (MachineType.Conveyor, 'V', 50), (MachineType.Agv, 'A', 10)]),
        ("MCH", [(MachineType.Cnc, 'C', 70), (MachineType.Conveyor, 'V', 20), (MachineType.Agv, 'A', 10)]),
        ("ASM", [(MachineType.Conveyor, 'V', 60), (MachineType.Agv, 'A', 40)]),
    ];

    public static IReadOnlyList<string> Shops { get; } = Plan.Select(p => p.Shop).ToArray();

    public static IReadOnlyList<MachineInfo> ForShop(string plant, string shop)
    {
        var mix = Plan.Single(p => p.Shop == shop).Mix;
        var list = new List<MachineInfo>();
        foreach (var (type, letter, count) in mix)
            for (var i = 0; i < count; i++)
            {
                var n = list.Count;
                list.Add(new MachineInfo($"{plant}-{shop}-{letter}{n + 1:000}", type, shop, n));
            }
        return list;
    }
}
