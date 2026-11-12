using AwesomeAssertions;
using EventDrivenDesign.Api;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Shared;
using Xunit;

namespace EventDrivenDesign.Tests;

public class OverviewBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly ProcessStats Proc = new(0, 0, 0);
    private static readonly string[] Shops = ["PRS", "BDY"];

    private static GatewayMetrics Gw(
        string shop,
        bool paused = false,
        double rate = 100,
        long buffered = 0,
        params (string, long)[] seqs
    ) =>
        new(
            $"gateway-{shop.ToLowerInvariant()}",
            shop,
            "Connected",
            paused,
            seqs.Length,
            seqs.Sum(s => s.Item2),
            buffered,
            0,
            0,
            rate,
            0,
            0,
            seqs.ToDictionary(s => s.Item1, s => s.Item2),
            Proc
        );

    private static ConsumerMetrics Cons(string id, double rate = 250) =>
        new(id, [0], rate, rate, 0, 0, 0, 0, Proc);

    private static OffsetSample Offsets(long end, long committed) =>
        new(new Dictionary<int, long> { [0] = end }, new Dictionary<int, long> { [0] = committed });

    /// <summary>Every source seen at <paramref name="seen"/>, running, no lag.</summary>
    private static Snapshot Fresh(
        DateTimeOffset seen,
        bool paused = false,
        long buffered = 0,
        long lag = 0,
        double mirrorLagSeconds = 0
    )
    {
        var s = new Snapshot(Shops, ["consumer-1", "consumer-2"]);
        s.SetGateway("PRS", Gw("PRS", paused, 100, buffered, ("P1-PRS-S001", 10)), seen);
        s.SetGateway("BDY", Gw("BDY", paused, 100, 0, ("P1-BDY-W001", 5)), seen);
        s.SetConsumer("consumer-1", Cons("consumer-1"), seen);
        s.SetConsumer("consumer-2", Cons("consumer-2"), seen);
        s.SetPlantEnd(1000, seen);
        s.SetCentral(Offsets(1000, 1000 - lag), seen);
        s.SetDb(
            new DbSample([new("P1-PRS-S001", 10, 10, Now), new("P1-BDY-W001", 5, 5, Now)], 15, 15),
            seen
        );
        s.SeenPlant(Now);
        s.SeenCentral(Now - TimeSpan.FromSeconds(mirrorLagSeconds));
        return s;
    }

    [Fact]
    public void Central_down_still_builds_an_overview()
    {
        var s = Fresh(Now - TimeSpan.FromSeconds(30));
        // plant side and gateways are alive; database, central cluster and consumers were last seen 30 s ago
        foreach (var shop in Shops)
            s.SetGateway(shop, Gw(shop), Now);
        s.SetPlantEnd(1000, Now);

        var o = OverviewBuilder.Build(s, Now);

        o.Database.Health.Should().BeOfType<Down>();
        o.CentralKafka.Health.Should().BeOfType<Down>();
        o.Consumers.Should().OnlyContain(c => c.Health is Down);
        o.PlantKafka.Health.Should().BeOfType<Healthy>();
        o.Gateways.Should().OnlyContain(g => g.Health is Healthy);
        o.At.Should().Be(Now);
        // the last known lag is 0, which must not read as "ETA 0": nothing is known while central is dark
        o.ConsumerLag.Should().Be(0);
        o.DrainEtaSeconds.Should().BeNull();
    }

    [Fact]
    public void A_stale_central_sample_gives_no_eta_even_with_a_backlog_and_live_consumers()
    {
        var s = Fresh(Now, lag: 3000);
        s.SetCentral(Offsets(1000, 0), Now - TimeSpan.FromSeconds(30));
        var o = OverviewBuilder.Build(s, Now);
        o.CentralKafka.Health.Should().BeOfType<Down>();
        o.ConsumerLag.Should().Be(1000); // last known value
        o.DrainEtaSeconds.Should().BeNull();
    }

    [Fact]
    public void Every_consumer_down_gives_no_eta()
    {
        var s = Fresh(Now, lag: 3000);
        s.SetConsumer("consumer-1", Cons("consumer-1"), Now - TimeSpan.FromSeconds(30));
        s.SetConsumer("consumer-2", Cons("consumer-2"), Now - TimeSpan.FromSeconds(30));
        OverviewBuilder.Build(s, Now).DrainEtaSeconds.Should().BeNull();
    }

    [Fact]
    public void An_older_started_database_sample_never_replaces_a_newer_one()
    {
        var s = new Snapshot(Shops, []);
        var newer = new DbSample([new("m1", 10, 10, Now)], 10, 10);
        var older = new DbSample([new("m1", 7, 10, Now)], 7, 10);
        s.SetDb(newer, Now + TimeSpan.FromSeconds(2));
        s.SetDb(older, Now);
        s.Db!.Value.Should().BeSameAs(newer);
        s.Db.At.Should().Be(Now + TimeSpan.FromSeconds(2));
        var newest = new DbSample([new("m1", 11, 11, Now)], 11, 11);
        s.SetDb(newest, Now + TimeSpan.FromSeconds(3));
        s.Db!.Value.Should().BeSameAs(newest);
    }

    [Fact]
    public void A_source_never_seen_is_down_not_an_exception()
    {
        var o = OverviewBuilder.Build(new Snapshot(Shops, ["consumer-1"]), Now);

        o.Gateways.Select(g => g.Shop).Should().Equal("PRS", "BDY");
        o.Gateways.Should().OnlyContain(g => g.Health is Down);
        o.Consumers.Single().ConsumerId.Should().Be("consumer-1");
        o.Database.Health.Should().BeOfType<Down>();
        o.PlantKafka.Health.Should().BeOfType<Down>();
        o.ConsumerLag.Should().Be(0);
        o.MirrorLagSeconds.Should().BeNull();
    }

    [Fact]
    public void Healthy_sources_give_rates_lag_and_a_drain_estimate()
    {
        var s = Fresh(Now - TimeSpan.FromSeconds(1), lag: 3000, mirrorLagSeconds: 4);

        var o = OverviewBuilder.Build(s, Now);

        o.Gateways.Should().OnlyContain(g => g.Health is Healthy);
        o.LiveRate.Should().Be(200);
        o.DrainRate.Should().Be(500);
        o.ConsumerLag.Should().Be(3000);
        o.MirrorLagSeconds.Should().Be(4);
        o.DrainEtaSeconds.Should().Be(10); // 3000 / (500 - 200)
        o.Database.Stored.Should().Be(15);
    }

    [Fact]
    public void Paused_gateways_do_not_count_towards_the_live_rate()
    {
        OverviewBuilder.Build(Fresh(Now, paused: true), Now).LiveRate.Should().Be(0);
    }

    [Fact]
    public void Degraded_after_three_seconds_and_down_after_ten()
    {
        OverviewBuilder
            .Build(Fresh(Now - TimeSpan.FromSeconds(5)), Now)
            .Database.Health.Should()
            .BeOfType<Degraded>();
        OverviewBuilder
            .Build(Fresh(Now - TimeSpan.FromSeconds(11)), Now)
            .Database.Health.Should()
            .BeOfType<Down>();
    }

    // Gateways went quiet 5 s ago, so the 1 s old offsets are well past the settle time; every source answered just now.
    private static Snapshot Settled(DateTimeOffset? dbAt = null)
    {
        var s = Fresh(Now - TimeSpan.FromSeconds(5), paused: true);
        s.SetGateway("PRS", Gw("PRS", true, 0, 0, ("P1-PRS-S001", 10)), Now);
        s.SetGateway("BDY", Gw("BDY", true, 0, 0, ("P1-BDY-W001", 5)), Now);
        s.SetCentral(Offsets(1000, 1000), Now);
        s.SetDb(
            new DbSample([new("P1-PRS-S001", 10, 10, Now), new("P1-BDY-W001", 5, 5, Now)], 15, 15),
            dbAt ?? Now
        );
        s.SetPlantEnd(1000, Now);
        return s;
    }

    [Fact]
    public void Paused_drained_and_matching_is_a_pass()
    {
        var r = OverviewBuilder.Reconcile(Settled(), Now, 0);
        r.Verdict.Should().Be("PASS");
        r.Generated.Should().Be(15);
        r.Missing.Should().Be(0);
        r.DuplicateRows.Should().Be(0);
        r.Paused.Should().BeTrue();
        r.Drained.Should().BeTrue();
    }

    [Fact]
    public void Running_gateways_wait()
    {
        OverviewBuilder.Reconcile(Fresh(Now), Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void A_gateway_that_just_went_quiet_waits_for_the_mirror_to_settle()
    {
        var s = Settled();
        s.SetGateway("BDY", Gw("BDY", false, 100, 0, ("P1-BDY-W001", 5)), Now);
        s.SetGateway("BDY", Gw("BDY", true, 0, 0, ("P1-BDY-W001", 5)), Now); // paused and empty only since Now
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void A_buffered_reading_waits_even_when_counts_match()
    {
        OverviewBuilder
            .Reconcile(Fresh(Now, paused: true, buffered: 1), Now, null)
            .Verdict.Should()
            .Be("WAIT");
    }

    [Fact]
    public void Consumer_lag_waits()
    {
        OverviewBuilder
            .Reconcile(Fresh(Now, paused: true, lag: 5), Now, null)
            .Verdict.Should()
            .Be("WAIT");
    }

    [Fact]
    public void Mirror_lag_waits()
    {
        var s = Settled();
        s.SeenPlant(Now + TimeSpan.FromSeconds(3));
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void A_stale_gateway_waits()
    {
        var s = Settled();
        s.SetGateway(
            "BDY",
            Gw("BDY", true, 0, 0, ("P1-BDY-W001", 5)),
            Now - TimeSpan.FromSeconds(20)
        );
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void A_database_that_is_down_waits()
    {
        OverviewBuilder
            .Reconcile(Settled(dbAt: Now - TimeSpan.FromSeconds(30)), Now, null)
            .Verdict.Should()
            .Be("WAIT");
    }

    [Fact]
    public void Offsets_taken_before_the_last_gateway_report_do_not_count_as_drained()
    {
        var s = Settled();
        s.SetCentral(Offsets(1000, 1000), Now - TimeSpan.FromSeconds(6));
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    // The 2 s offsets poll can finish while the endpoint's DB query is running: the newest offsets are then a few ms newer
    // than the DB sample, though the offsets themselves have not changed since the first of them.
    [Fact]
    public void Unchanged_offsets_sampled_again_after_the_database_started_still_pass()
    {
        var s = Settled(dbAt: Now - TimeSpan.FromMilliseconds(5));
        s.SetCentral(Offsets(1000, 1000), Now - TimeSpan.FromSeconds(2));
        s.SetCentral(Offsets(1000, 1000), Now);
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("PASS");
    }

    [Fact]
    public void A_record_that_lands_after_the_database_started_waits()
    {
        var s = Settled(dbAt: Now - TimeSpan.FromSeconds(1));
        s.SetCentral(Offsets(1000, 1000), Now - TimeSpan.FromSeconds(2));
        s.SetCentral(Offsets(1001, 1001), Now);
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void Lag_that_reappeared_after_the_database_started_waits()
    {
        var s = Settled(dbAt: Now - TimeSpan.FromSeconds(2));
        s.SetCentral(Offsets(1000, 1000), Now - TimeSpan.FromSeconds(3));
        s.SetCentral(Offsets(1000, 990), Now - TimeSpan.FromSeconds(1));
        s.SetCentral(Offsets(1000, 1000), Now);
        OverviewBuilder.Reconcile(s, Now, null).Verdict.Should().Be("WAIT");
    }

    [Fact]
    public void Lost_readings_fail_once_paused_and_drained()
    {
        var s = Settled();
        s.SetDb(
            new DbSample([new("P1-PRS-S001", 9, 10, Now), new("P1-BDY-W001", 5, 5, Now)], 14, 12),
            Now
        );
        var r = OverviewBuilder.Reconcile(s, Now, null);
        r.Verdict.Should().Be("FAIL");
        r.Missing.Should().Be(1);
        r.Gaps.Should().Be(1);
        r.DuplicatesAbsorbed.Should().Be(2);
    }

    [Fact]
    public void Both_tails_empty_while_paused_counts_as_no_mirror_lag()
    {
        var later = Now + TimeSpan.FromSeconds(5);
        var s = new Snapshot(Shops, []);
        foreach (var shop in Shops)
        {
            s.SetGateway(shop, Gw(shop, true, 0, 0, ($"P1-{shop}-X001", 1)), Now);
            s.SetGateway(shop, Gw(shop, true, 0, 0, ($"P1-{shop}-X001", 1)), later);
        }
        s.SetPlantEnd(10, later);
        s.SetCentral(Offsets(10, 10), later);
        s.SetDb(
            new DbSample([new("P1-PRS-X001", 1, 1, Now), new("P1-BDY-X001", 1, 1, Now)], 2, 2),
            later
        );
        OverviewBuilder.Reconcile(s, later, null).Verdict.Should().Be("PASS");
    }

    [Fact]
    public void Chart_points_after_the_watermark_are_provisional()
    {
        var w = Now;
        var chart = OverviewBuilder.BuildChart(
            [(w - TimeSpan.FromSeconds(5), 7), (w, 8), (w + TimeSpan.FromSeconds(5), 9)],
            w
        );
        chart.Watermark.Should().Be(w);
        chart.Points.Select(p => p.Provisional).Should().Equal(false, false, true);
        OverviewBuilder.BuildChart([(w, 1)], null).Points.Single().Provisional.Should().BeTrue();
    }

    [Fact]
    public void A_good_machine_id_routes_to_its_shop_gateway()
    {
        ApiRoutes.GatewayFor("P1-BDY-W001").Value.Should().Be("gateway-bdy:8080");
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("P1-XXX-W001")]
    [InlineData("P1-BDY")]
    [InlineData("")]
    public void A_bad_machine_id_is_invalid(string id)
    {
        var r = ApiRoutes.GatewayFor(id);
        r.IsSuccess.Should().BeFalse();
        r.Error!.Code.Should().Be("api.machine.bad_id");
        r.Error.Kind.Should().Be(ErrorKind.Invalid);
    }
}
