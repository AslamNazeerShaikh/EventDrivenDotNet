using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using Xunit;

namespace EventDrivenDesign.Tests;

public class SimulatorTests
{
    public static TheoryData<MachineType> AllTypes => new(Enum.GetValues<MachineType>());

    private static MachineSimulator Sim(string shop, int index, int seed) =>
        new(MachineCatalog.ForShop("P1", shop)[index], seed);

    [Fact]
    public void Body_shop_has_the_spec_mix()
    {
        var m = MachineCatalog.ForShop("P1", "BDY");
        m.Should().HaveCount(100);
        m[41].MachineId.Should().Be("P1-BDY-W042");
        m[80].MachineId.Should().Be("P1-BDY-V081");
        m[99].MachineId.Should().Be("P1-BDY-A100");
        m.Count(x => x.Type == MachineType.WeldRobot).Should().Be(80);
        m[41].Index.Should().Be(41);
    }

    [Fact]
    public void Plant_has_500_unique_ids() =>
        MachineCatalog
            .Shops.SelectMany(s => MachineCatalog.ForShop("P1", s))
            .Select(m => m.MachineId)
            .Distinct()
            .Should()
            .HaveCount(500);

    [Fact]
    public void Shops_are_the_five_spec_shops() =>
        MachineCatalog.Shops.Should().Equal("PRS", "BDY", "PNT", "MCH", "ASM");

    [Fact]
    public void Same_seed_same_series()
    {
        var a = Sim("BDY", 41, 2041);
        var b = Sim("BDY", 41, 2041);
        for (var i = 0; i < 100; i++)
            a.Next().Signals.Should().BeEquivalentTo(b.Next().Signals);
    }

    [Fact]
    public void Different_seed_different_series()
    {
        var a = Sim("BDY", 41, 2041);
        var b = Sim("BDY", 41, 2042);
        var differs = false;
        for (var i = 0; i < 100; i++)
            differs |= !a.Next().Signals.SequenceEqual(b.Next().Signals);
        differs.Should().BeTrue();
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Without_a_fault_values_stay_in_band_and_never_alarm(MachineType t)
    {
        var sim = new MachineSimulator(new MachineInfo("X", t, "S", 0), 7);
        var specs = Signals.For(t);
        for (var i = 0; i < 10_000; i++)
        {
            var tick = sim.Next();
            tick.Alarms.Should().BeEmpty();
            foreach (var s in specs)
                tick.Signals[s.Name].Should().BeInRange(s.Min, s.Max);
        }
    }

    [Fact]
    public void Overheat_fault_on_weld_robot()
    {
        var sim = Sim("BDY", 41, 2041);
        sim.InjectFault(AlarmKind.Overheat).Should().BeTrue();
        var ticks = Enumerable.Range(0, 11).Select(_ => sim.Next()).ToList();
        ticks
            .Take(10)
            .Should()
            .AllSatisfy(t => t.Signals["motor_temp_c"].Should().BeApproximately(88, 1e-6));
        ticks[10].Signals["motor_temp_c"].Should().BeInRange(40, 70);
        var alarm = ticks.SelectMany(t => t.Alarms).Should().ContainSingle().Subject;
        (alarm.Kind, alarm.Severity, alarm.Signal, alarm.Threshold)
            .Should()
            .Be((AlarmKind.Overheat, "critical", "motor_temp_c", (double?)85.0));
        alarm.Value.Should().BeApproximately(88, 1e-6);
        ticks[0].Alarms.Should().ContainSingle();
    }

    [Fact]
    public void Injecting_into_an_already_faulted_signal_changes_nothing()
    {
        var sim = Sim("BDY", 41, 2041);
        sim.InjectFault(AlarmKind.Overheat).Should().BeTrue();
        var ticks = new List<SimTick> { sim.Next() };
        sim.InjectFault(AlarmKind.Overheat).Should().BeTrue();
        ticks.AddRange(Enumerable.Range(0, 10).Select(_ => sim.Next()));
        ticks.SelectMany(t => t.Alarms).Should().ContainSingle();
        ticks[10].Signals["motor_temp_c"].Should().BeInRange(40, 70);
    }

    [Fact]
    public void Lower_bound_only_spec_faults_below_the_threshold()
    {
        var sim = Sim("ASM", 60, 1);
        sim.InjectFault(AlarmKind.LowBattery).Should().BeTrue();
        var tick = sim.Next();
        tick.Signals["battery_pct"].Should().BeApproximately(15 - 0.1 * 80, 1e-6);
        var alarm = tick.Alarms.Should().ContainSingle().Subject;
        (alarm.Severity, alarm.Threshold).Should().Be(("warning", (double?)15.0));
    }

    [Fact]
    public void EStop_does_not_apply_to_a_weld_robot() =>
        Sim("BDY", 0, 1).InjectFault(AlarmKind.EStop).Should().BeFalse();

    [Fact]
    public void EStop_on_agv_sets_e_stop_for_10_ticks()
    {
        var sim = Sim("ASM", 60, 1);
        sim.InjectFault(AlarmKind.EStop).Should().BeTrue();
        var ticks = Enumerable.Range(0, 11).Select(_ => sim.Next()).ToList();
        ticks
            .Take(10)
            .Should()
            .AllSatisfy(t => t.Signals["e_stop"].Should().BeApproximately(1, 1e-6));
        ticks[10].Signals["e_stop"].Should().Be(0);
        var alarm = ticks.SelectMany(t => t.Alarms).Should().ContainSingle().Subject;
        (alarm.Kind, alarm.Severity).Should().Be((AlarmKind.EStop, "critical"));
    }

    [Fact]
    public void Overload_is_high_and_other_kinds_are_warnings()
    {
        var press = Sim("PRS", 0, 1);
        press.InjectFault(AlarmKind.Overload);
        press.Next().Alarms.Single().Severity.Should().Be("high");
        var cnc = Sim("MCH", 0, 1);
        cnc.InjectFault(AlarmKind.Vibration);
        cnc.Next().Alarms.Single().Severity.Should().Be("warning");
    }
}
