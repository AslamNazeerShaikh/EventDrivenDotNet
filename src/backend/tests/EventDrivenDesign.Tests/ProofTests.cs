using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using Xunit;

namespace EventDrivenDesign.Tests;

public class ProofTests
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static ProgressRow Row(string id, long stored, long maxSeq, int minute = 0) =>
        new(id, stored, maxSeq, T.AddMinutes(minute));

    private static readonly Dictionary<string, long> Generated = new() { ["A"] = 10, ["B"] = 5 };
    private static readonly ProgressRow[] Complete = [Row("A", 10, 10), Row("B", 5, 5)];

    [Fact]
    public void Matching_counts_when_paused_and_drained_pass()
    {
        var r = Proof.Reconcile(Generated, Complete, 0, 0, allPaused: true, drained: true);
        r.Verdict.Should().Be("PASS");
        r.Missing.Should().Be(0);
        r.Gaps.Should().Be(0);
        r.Generated.Should().Be(15);
        r.Stored.Should().Be(15);
    }

    [Fact]
    public void Matching_counts_still_wait_while_in_flight()
    {
        Proof.Reconcile(Generated, Complete, 0, 0, allPaused: true, drained: false).Verdict.Should().Be("WAIT");
        Proof.Reconcile(Generated, Complete, 0, 0, allPaused: false, drained: true).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void A_hole_fails_once_drained()
    {
        var r = Proof.Reconcile(new Dictionary<string, long> { ["B"] = 5 }, [Row("B", 4, 5)], 0, null, true, true);
        r.Missing.Should().Be(1);
        r.Gaps.Should().Be(1);
        r.Verdict.Should().Be("FAIL");
    }

    [Fact]
    public void Machine_with_nothing_stored_is_all_missing()
    {
        var r = Proof.Reconcile(new Dictionary<string, long> { ["C"] = 3 }, [], 0, null, true, true);
        r.Missing.Should().Be(3);
        r.Gaps.Should().Be(0);
        r.Verdict.Should().Be("FAIL");
    }

    [Fact]
    public void Unknown_machine_is_reported_and_does_not_hide_missing()
    {
        var r = Proof.Reconcile(new Dictionary<string, long> { ["A"] = 10 }, [Row("A", 8, 8), Row("Z", 2, 2)], 0, null, true, true);
        r.Stored.Should().Be(10);
        r.Missing.Should().Be(2);
        r.Machines.Select(m => m.MachineId).Should().Equal("A", "Z");
        r.Machines[1].Missing.Should().Be(0);
        r.Verdict.Should().Be("FAIL");
    }

    [Fact]
    public void Drain_eta()
    {
        Proof.DrainEtaSeconds(60000, 2000, 500).Should().Be(40.0);
        Proof.DrainEtaSeconds(100, 500, 500).Should().BeNull();
        Proof.DrainEtaSeconds(0, 0, 500).Should().Be(0);
    }

    [Fact]
    public void Watermark_ignores_machines_with_gaps()
    {
        Proof.Watermark([Row("A", 10, 10, 5), Row("B", 5, 5, 3), Row("C", 4, 5, 1)])
            .Should().Be(T.AddMinutes(3));
        Proof.Watermark([Row("C", 4, 5, 1)]).Should().BeNull();
    }

    [Fact]
    public void Health_by_age()
    {
        Proof.HealthFromAge(TimeSpan.FromSeconds(2), T).Should().BeOfType<Healthy>();
        var degraded = Proof.HealthFromAge(TimeSpan.FromSeconds(5), T).Should().BeOfType<Degraded>().Subject;
        degraded.Reason.Should().Be("no update for 5 s");
        Proof.HealthFromAge(TimeSpan.FromSeconds(12), T).Should().BeOfType<Down>().Which.LastSeen.Should().Be(T);
    }

    [Fact]
    public void Health_boundaries_are_3_and_10_seconds()
    {
        Proof.HealthFromAge(TimeSpan.FromSeconds(3), T).Should().BeOfType<Degraded>();
        Proof.HealthFromAge(TimeSpan.FromSeconds(10), T).Should().BeOfType<Down>();
    }
}
