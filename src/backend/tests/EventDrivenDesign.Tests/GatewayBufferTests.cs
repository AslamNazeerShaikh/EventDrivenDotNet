using AwesomeAssertions;
using EventDrivenDesign.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EventDrivenDesign.Tests;

public sealed class GatewayBufferTests : IDisposable
{
    private const string Machine = "P1-BDY-W042";
    private readonly string _dir = Directory.CreateTempSubdirectory("gwbuf").FullName;

    private string DbPath => Path.Combine(_dir, "buffer.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static OutboxRow Row(Lane lane, long seq, long timeMs) =>
        new(0, lane, Machine, seq, timeMs, [1, 2, 3]);

    private static TickBatch Tick(long lastSeq, long lastAlarmSeq, long timeMs, params OutboxRow[] rows) =>
        new(rows, [new MachineState(Machine, lastSeq, lastAlarmSeq, timeMs)]);

    [Fact]
    public async Task New_file_uses_wal_full_and_incremental_vacuum()
    {
        await using (var buffer = GatewayBuffer.Open(DbPath))
        {
            buffer.IsNew.Should().BeTrue();
        }

        using var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        cmd.ExecuteScalar().Should().Be("wal");
        cmd.CommandText = "PRAGMA auto_vacuum";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(2);
        cmd.CommandText = "PRAGMA synchronous";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(2);
    }

    [Fact]
    public async Task Alarms_come_first_then_readings_oldest_first()
    {
        await using var buffer = GatewayBuffer.Open(DbPath);
        await buffer.WriteTickAsync(
            Tick(3, 1, 1000, Row(Lane.Reading, 1, 1000), Row(Lane.Reading, 2, 1000), Row(Lane.Reading, 3, 1000), Row(Lane.Alarm, 1, 1000)),
            TestContext.Current.CancellationToken);
        await buffer.WriteTickAsync(
            Tick(6, 1, 2000, Row(Lane.Reading, 4, 2000), Row(Lane.Reading, 5, 2000), Row(Lane.Reading, 6, 2000)),
            TestContext.Current.CancellationToken);

        var rows = buffer.ReadOldest(10);

        rows.Select(r => (r.Lane, r.Seq)).Should().Equal(
            (Lane.Alarm, 1L), (Lane.Reading, 1L), (Lane.Reading, 2L), (Lane.Reading, 3L),
            (Lane.Reading, 4L), (Lane.Reading, 5L), (Lane.Reading, 6L));
    }

    [Fact]
    public async Task Reopen_keeps_rows_and_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new MachineState(Machine, 7, 2, 1000);
        await using (var buffer = GatewayBuffer.Open(DbPath))
        {
            await buffer.WriteTickAsync(Tick(7, 2, 1000, Row(Lane.Reading, 7, 1000), Row(Lane.Alarm, 2, 1000)), ct);
        }

        await using var reopened = GatewayBuffer.Open(DbPath);

        reopened.IsNew.Should().BeFalse();
        reopened.LoadState()[Machine].Should().Be(expected);
        reopened.ReadOldest(10).Select(r => (r.Lane, r.Seq, r.MachineId, r.DeviceTimeMs)).Should().Equal(
            (Lane.Alarm, 2L, Machine, 1000L), (Lane.Reading, 7L, Machine, 1000L));
        reopened.ReadOldest(10)[0].Payload.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task Delete_removes_only_the_given_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var buffer = GatewayBuffer.Open(DbPath);
        await buffer.WriteTickAsync(
            Tick(3, 0, 1000, Row(Lane.Reading, 1, 1000), Row(Lane.Reading, 2, 1000), Row(Lane.Reading, 3, 1000)), ct);
        var rows = buffer.ReadOldest(10);

        await buffer.DeleteAsync([rows[0].Id, rows[2].Id], ct);

        buffer.ReadOldest(10).Select(r => r.Seq).Should().Equal(2L);
    }

    [Fact]
    public async Task Paused_survives_reopen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var buffer = GatewayBuffer.Open(DbPath))
        {
            buffer.LoadPaused().Should().BeFalse();
            await buffer.SetPausedAsync(true, ct);
        }

        await using var reopened = GatewayBuffer.Open(DbPath);
        reopened.LoadPaused().Should().BeTrue();
    }

    [Fact]
    public async Task Stats_counts_lanes_and_oldest_device_time()
    {
        await using var buffer = GatewayBuffer.Open(DbPath);
        await buffer.WriteTickAsync(
            Tick(3, 1, 1000, Row(Lane.Reading, 1, 1500), Row(Lane.Reading, 2, 1200), Row(Lane.Reading, 3, 1800), Row(Lane.Alarm, 1, 1300)),
            TestContext.Current.CancellationToken);

        var stats = buffer.Stats();

        stats.Readings.Should().Be(3);
        stats.Alarms.Should().Be(1);
        stats.OldestDeviceTimeMs.Should().Be(1200);
        stats.FileBytes.Should().BeGreaterThan(0);
    }
}
