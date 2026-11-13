using System.Text.Json;
using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Persistence;
using Xunit;

namespace EventDrivenDesign.Tests;

public class CollectorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
    private static readonly IReadOnlyDictionary<string, MachineState> NoState =
        new Dictionary<string, MachineState>();

    private static Collector New(IReadOnlyDictionary<string, MachineState>? resume = null) =>
        new("P1", "BDY", 7, resume ?? NoState);

    private static IReadOnlyDictionary<string, MachineState> ResumeAt(
        DateTimeOffset lastTick,
        long lastSeq
    ) =>
        MachineCatalog
            .ForShop("P1", "BDY")
            .ToDictionary(
                m => m.MachineId,
                m => new MachineState(
                    m.MachineId,
                    lastSeq,
                    0,
                    lastTick.ToUnixTimeMilliseconds() + m.Index * 10
                )
            );

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
    public void Resume_restarts_numbering_from_the_given_state()
    {
        var collector = New();
        for (var i = 0; i < 3; i++)
            collector.Tick(T0.AddSeconds(i));
        collector.Resume(
            new Dictionary<string, MachineState>
            {
                ["P1-BDY-W042"] = new(
                    "P1-BDY-W042",
                    200,
                    0,
                    T0.AddSeconds(2).ToUnixTimeMilliseconds()
                ),
            }
        );
        var batch = collector.Tick(T0.AddSeconds(3));
        Reading(batch, "P1-BDY-W042").Seq.Should().Be(201);
        Reading(batch, "P1-BDY-W001").Seq.Should().Be(4);
    }

    [Fact]
    public void Resume_continues_numbering()
    {
        var resume = new Dictionary<string, MachineState>
        {
            ["P1-BDY-W042"] = new("P1-BDY-W042", 41, 0, 0),
        };
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
        for (var i = 0; i < 3; i++)
            collector.Tick(T0.AddSeconds(i));
        collector.Generated.Should().Be(300);
        collector.LastSeqByMachine["P1-BDY-W042"].Should().Be(3);
    }

    [Fact]
    public void Backfill_fills_a_120_second_gap()
    {
        var collector = New(ResumeAt(T0, 10));
        var batches = collector.Backfill(T0.AddSeconds(120), 600).ToList();

        batches.Should().HaveCount(2);
        batches[0].Rows.Should().OnlyContain(r => r.Lane == Lane.Reading);
        batches[1].Rows.Should().OnlyContain(r => r.Lane == Lane.Reading);
        batches[0].States.Should().OnlyContain(s => s.LastSeq == 70);
        batches[^1].States.Should().OnlyContain(s => s.LastSeq == 130);
        var baseMs = T0.ToUnixTimeMilliseconds();
        foreach (var m in MachineCatalog.ForShop("P1", "BDY"))
        {
            var rows = batches
                .SelectMany(b => b.Rows.Where(r => r.MachineId == m.MachineId))
                .OrderBy(r => r.DeviceTimeMs)
                .ToList();
            rows.Should().HaveCount(120);
            rows.Select(r => r.Seq).Should().Equal(Enumerable.Range(11, 120).Select(i => (long)i));
            rows.Select(r => r.DeviceTimeMs)
                .Should()
                .Equal(Enumerable.Range(1, 120).Select(k => baseMs + k * 1000L + m.Index * 10));
        }
        collector.LastSeqByMachine["P1-BDY-W042"].Should().Be(130);
        collector.Generated.Should().Be(13_000);
    }

    [Fact]
    public void Backfill_past_the_depth_records_one_DataGap()
    {
        var machines = MachineCatalog.ForShop("P1", "BDY");
        var collector = New(ResumeAt(T0, 10));
        var batches = collector.Backfill(T0.AddSeconds(900), 600).ToList();

        batches.Should().HaveCount(10);
        var baseMs = T0.ToUnixTimeMilliseconds();
        foreach (var m in machines)
        {
            var rows = batches
                .SelectMany(b => b.Rows.Where(r => r.MachineId == m.MachineId))
                .Where(r => r.Lane == Lane.Reading)
                .OrderBy(r => r.DeviceTimeMs)
                .ToList();
            rows.Should().HaveCount(600);
            rows.Select(r => r.Seq).Should().Equal(Enumerable.Range(11, 600).Select(i => (long)i));
            rows.First().DeviceTimeMs.Should().Be(baseMs + 301_000 + m.Index * 10);
            rows.Last().DeviceTimeMs.Should().Be(baseMs + 900_000 + m.Index * 10);
        }

        var gaps = batches[0].Rows.Where(r => r.Lane == Lane.Alarm).ToList();
        gaps.Should().HaveCount(100);
        foreach (var row in gaps)
        {
            var m = machines.Single(x => x.MachineId == row.MachineId);
            row.DeviceTimeMs.Should().Be(baseMs + 1000 + m.Index * 10);
            var alarm = JsonSerializer.Deserialize<MachineAlarm>(row.Payload, Json.Options)!;
            alarm.Kind.Should().Be(AlarmKind.DataGap);
            alarm.Severity.Should().Be("warning");
            alarm.Signal.Should().BeNull();
            alarm.Value.Should().Be(300);
            alarm.Threshold.Should().Be(600);
            alarm.ReadingSeq.Should().Be(10);
            alarm.AlarmSeq.Should().Be(1);
        }
        batches.Skip(1).SelectMany(b => b.Rows).Should().OnlyContain(r => r.Lane == Lane.Reading);
        batches[^1].States.Should().OnlyContain(s => s.LastSeq == 610);
    }

    [Fact]
    public void Backfill_skips_machines_never_numbered() =>
        New().Backfill(T0.AddSeconds(900), 600).Should().BeEmpty();

    [Fact]
    public void Backfill_within_a_second_yields_nothing() =>
        New(ResumeAt(T0, 10)).Backfill(T0.AddMilliseconds(900), 600).Should().BeEmpty();

    [Fact]
    public void Backfill_after_one_live_tick_backfills_without_a_resume_state()
    {
        var collector = New();
        collector.Tick(T0);

        var batches = collector.Backfill(T0.AddSeconds(10), 600).ToList();

        batches.Should().HaveCount(1);
        var readings = batches[0].Rows.Where(r => r.Lane == Lane.Reading).ToList();
        readings.Should().HaveCount(1000);
        readings.Should().OnlyContain(r => r.Seq >= 2 && r.Seq <= 11);
        collector.LastSeqByMachine["P1-BDY-W042"].Should().Be(11);
    }
}
