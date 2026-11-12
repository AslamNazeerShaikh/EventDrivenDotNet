using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EventDrivenDesign.IntegrationTests;

public sealed class CentralStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private NpgsqlDataSource _db = null!;
    private CentralStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync(TestContext.Current.CancellationToken);
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await using var cmd = _db.CreateCommand(await File.ReadAllTextAsync("01-schema.sql", TestContext.Current.CancellationToken));
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _store = new CentralStore(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    private static MachineReading Reading(string machine, long seq) => new(
        machine, "press", "shop-1", seq, T0.AddSeconds(seq), new Dictionary<string, double> { ["temp_c"] = 20.5 + seq });

    private static List<MachineReading> Hundred() =>
        [.. Enumerable.Range(1, 10).SelectMany(m => Enumerable.Range(1, 10).Select(s => Reading($"m{m}", s)))];

    private async Task<T> Scalar<T>(string sql)
    {
        await using var cmd = _db.CreateCommand(sql);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Every_reading_twice_is_stored_once()
    {
        var ct = TestContext.Current.CancellationToken;
        (await _store.InsertReadingsAsync(Hundred(), [], "w1", ct)).Should().Be(100);
        (await _store.InsertReadingsAsync(Hundred(), [], "w1", ct)).Should().Be(0);

        (await Scalar<long>("SELECT count(*) FROM readings")).Should().Be(100);
        (await Scalar<long>("SELECT received FROM ingest_totals WHERE stream='readings' AND writer_id='w1'")).Should().Be(200);
        (await Scalar<long>("SELECT inserted FROM ingest_totals WHERE stream='readings' AND writer_id='w1'")).Should().Be(100);
        (await Scalar<long>("SELECT count(*) FROM machine_progress WHERE stored = 10 AND max_seq = 10")).Should().Be(10);
        (await Scalar<long>("SELECT count(*) FROM machine_progress")).Should().Be(10);
        (await Scalar<string>("SELECT signals->>'temp_c' FROM readings WHERE machine_id='m1' AND seq=1")).Should().Be("21.5");
    }

    [Fact]
    public async Task Duplicates_inside_one_batch_are_absorbed()
    {
        var r = Reading("m1", 1);
        (await _store.InsertReadingsAsync([r, r], [], "w1", TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Out_of_order_seqs_still_add_up()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.InsertReadingsAsync([Reading("m1", 3)], [], "w1", ct);
        await _store.InsertReadingsAsync([Reading("m1", 1), Reading("m1", 2)], [], "w1", ct);

        (await Scalar<long>("SELECT stored FROM machine_progress WHERE machine_id='m1'")).Should().Be(3);
        (await Scalar<long>("SELECT max_seq FROM machine_progress WHERE machine_id='m1'")).Should().Be(3);
        (await Scalar<DateTime>("SELECT max_device_time FROM machine_progress WHERE machine_id='m1'"))
            .Should().Be(T0.AddSeconds(3).UtcDateTime);
    }

    [Fact]
    public async Task Alarms_are_stored_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var alarm = new MachineAlarm("m1", 1, 5, T0, AlarmKind.Overheat, "critical", "temp_c", 99.0, 90.0);
        (await _store.InsertAlarmsAsync([alarm], "w1", ct)).Should().Be(1);
        (await _store.InsertAlarmsAsync([alarm, alarm], "w1", ct)).Should().Be(0);

        (await Scalar<long>("SELECT count(*) FROM alarms")).Should().Be(1);
        (await Scalar<string>("SELECT kind FROM alarms")).Should().Be("Overheat");
        (await Scalar<long>("SELECT received FROM ingest_totals WHERE stream='alarms'")).Should().Be(3);
    }

    [Fact]
    public async Task Rejected_records_are_kept_once()
    {
        var ct = TestContext.Current.CancellationToken;
        RejectedRecord[] bad = [new("plant1.readings", 2, 41, [1, 2, 3], "bad json")];
        (await _store.InsertReadingsAsync([], bad, "w1", ct)).Should().Be(0);
        (await _store.InsertReadingsAsync([], bad, "w1", ct)).Should().Be(0);

        (await Scalar<long>("SELECT count(*) FROM rejected_records")).Should().Be(1);
        (await Scalar<byte[]>("SELECT raw FROM rejected_records")).Should().Equal(1, 2, 3);
    }
}
