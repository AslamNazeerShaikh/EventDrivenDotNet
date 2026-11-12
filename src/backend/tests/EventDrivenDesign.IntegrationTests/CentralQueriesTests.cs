using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EventDrivenDesign.IntegrationTests;

public sealed class CentralQueriesTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private NpgsqlDataSource _db = null!;
    private CentralStore _store = null!;
    private CentralQueries _queries = null!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync(TestContext.Current.CancellationToken);
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await using var cmd = _db.CreateCommand(await File.ReadAllTextAsync("01-schema.sql", TestContext.Current.CancellationToken));
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _store = new CentralStore(_db);
        _queries = new CentralQueries(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    private static MachineReading Reading(string machine, long seq, DateTimeOffset at) =>
        new(machine, "press", "shop-1", seq, at, new Dictionary<string, double> { ["temp_c"] = 20.5 });

    [Fact]
    public async Task Chart_buckets_by_device_time()
    {
        var ct = TestContext.Current.CancellationToken;
        // a five-second-aligned start a minute ago, so every reading is inside the window
        var now = DateTimeOffset.UtcNow;
        var start = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 5 * 5 - 60);
        // 5 readings in window 1, 4 in window 2, 3 in window 3
        int[] offsets = [0, 0, 1, 2, 4, 5, 6, 7, 9, 10, 12, 14];
        await _store.InsertReadingsAsync(
            [.. offsets.Select((o, i) => Reading("m1", i + 1, start.AddSeconds(o)))], [], "w1", ct);

        var chart = await _queries.ChartAsync(10, ct);

        chart.Should().Equal(
            (start, 5L), (start.AddSeconds(5), 4L), (start.AddSeconds(10), 3L));
    }

    [Fact]
    public async Task Chart_ignores_readings_older_than_the_window()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.InsertReadingsAsync([Reading("m1", 1, DateTimeOffset.UtcNow.AddMinutes(-30))], [], "w1", ct);
        (await _queries.ChartAsync(10, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Progress_and_duplicate_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;
        List<MachineReading> hundred =
            [.. Enumerable.Range(1, 10).SelectMany(m => Enumerable.Range(1, 10).Select(s => Reading($"m{m}", s, t0.AddSeconds(s))))];
        await _store.InsertReadingsAsync(hundred, [], "w1", ct);
        await _store.InsertReadingsAsync(hundred, [], "w1", ct);

        var progress = await _queries.ProgressAsync(ct);

        progress.Should().HaveCount(10);
        progress.Should().OnlyContain(p => p.Stored == 10 && p.MaxSeq == 10);
        (await _queries.DuplicateRowsAsync(ct)).Should().Be(0);
        (await _queries.TotalsAsync("readings", ct)).Should().Be((200L, 100L));
        (await _queries.TotalsAsync("alarms", ct)).Should().Be((0L, 0L));
    }

    [Fact]
    public async Task Recent_alarms_carry_their_latency_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-30);
        await _store.InsertAlarmsAsync([new("m1", 1, 5, t0, AlarmKind.Overheat, "critical", "temp_c", 99, 90)], [], "w1", ct);
        await Task.Delay(50, ct);
        await _store.InsertAlarmsAsync([new("m2", 1, 5, t0, AlarmKind.EStop, "critical", null, null, null)], [], "w1", ct);

        var alarms = await _queries.RecentAlarmsAsync(50, ct);

        alarms.Select(a => (a.MachineId, a.Kind)).Should().Equal(("m2", AlarmKind.EStop), ("m1", AlarmKind.Overheat));
        alarms.Should().OnlyContain(a => a.LatencySeconds >= 29 && a.LatencySeconds < 40);
        (await _queries.RecentAlarmsAsync(1, ct)).Should().ContainSingle();
    }
}
