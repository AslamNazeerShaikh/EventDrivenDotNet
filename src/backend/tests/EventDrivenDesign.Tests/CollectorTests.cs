using System.Text.Json;
using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Persistence;
using Xunit;

namespace EventDrivenDesign.Tests;

public class CollectorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
    private static readonly IReadOnlyDictionary<string, MachineState> NoState = new Dictionary<string, MachineState>();

    private static Collector New(IReadOnlyDictionary<string, MachineState>? resume = null) =>
        new("P1", "BDY", 7, resume ?? NoState);

    private static OutboxRow Reading(TickBatch b, string id) =>
        b.Rows.Single(r => r.Lane == Lane.Reading && r.MachineId == id);

    [Fact]
    public void First_tick_numbers_every_machine_from_1()
    {
        var batch = New().Tick(T0);
        var readings = batch.Rows.Where(r => r.Lane == Lane.Reading).ToList();
        readings.Should().HaveCount(100).And.OnlyContain(r => r.Seq == 1);
        Reading(batch, "P1-BDY-W042").DeviceTimeMs.Should().Be(T0.ToUnixTimeMilliseconds() + 410);
        batch.States.Should().HaveCount(100).And.OnlyContain(s => s.LastSeq == 1);
    }

    [Fact]
    public void Resume_continues_numbering()
    {
        var resume = new Dictionary<string, MachineState> { ["P1-BDY-W042"] = new("P1-BDY-W042", 41, 0, 0) };
        var batch = New(resume).Tick(T0);
        Reading(batch, "P1-BDY-W042").Seq.Should().Be(42);
        Reading(batch, "P1-BDY-W001").Seq.Should().Be(1);
    }

    [Fact]
    public void Payload_is_a_machine_reading()
    {
        var row = Reading(New().Tick(T0), "P1-BDY-W042");
        var reading = JsonSerializer.Deserialize<MachineReading>(row.Payload, Json.Options)!;
        reading.Shop.Should().Be("BDY");
        reading.MachineType.Should().Be("WeldRobot");
        reading.Signals.Should().HaveCount(3);
        reading.Seq.Should().Be(1);
    }

    [Fact]
    public void Injected_fault_yields_one_alarm_row()
    {
        var collector = New();
        collector.InjectFault("P1-BDY-W042", AlarmKind.Overheat).Should().BeTrue();
        var batch = collector.Tick(T0);
        var alarm = batch.Rows.Should().ContainSingle(r => r.Lane == Lane.Alarm).Subject;
        alarm.Seq.Should().Be(1);
        var payload = JsonSerializer.Deserialize<MachineAlarm>(alarm.Payload, Json.Options)!;
        payload.ReadingSeq.Should().Be(Reading(batch, "P1-BDY-W042").Seq);
        payload.AlarmSeq.Should().Be(1);
        alarm.DeviceTimeMs.Should().Be(Reading(batch, "P1-BDY-W042").DeviceTimeMs);
        batch.States.Single(s => s.MachineId == "P1-BDY-W042").LastAlarmSeq.Should().Be(1);
    }

    [Fact]
    public void Invalid_faults_are_rejected()
    {
        var collector = New();
        collector.InjectFault("P1-BDY-X999", AlarmKind.Overheat).Should().BeFalse();
        collector.InjectFault("P1-BDY-W042", AlarmKind.EStop).Should().BeFalse();
        collector.Tick(T0).Rows.Should().NotContain(r => r.Lane == Lane.Alarm);
    }

    [Fact]
    public void Generated_counts_all_readings()
    {
        var collector = New();
        for (var i = 0; i < 3; i++) collector.Tick(T0.AddSeconds(i));
        collector.Generated.Should().Be(300);
        collector.LastSeqByMachine["P1-BDY-W042"].Should().Be(3);
    }
}
