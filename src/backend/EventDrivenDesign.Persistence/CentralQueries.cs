using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using Npgsql;

namespace EventDrivenDesign.Persistence;

/// <summary>Read side of the central database, for the control room (doc 00 §8).</summary>
public sealed class CentralQueries(NpgsqlDataSource db)
{
    public async Task<IReadOnlyList<ProgressRow>> ProgressAsync(CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("SELECT machine_id, stored, max_seq, max_device_time FROM machine_progress");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<ProgressRow>();
        while (await reader.ReadAsync(ct))
            rows.Add(new ProgressRow(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), Utc(reader.GetDateTime(3))));
        return rows;
    }

    public async Task<(long Received, long Inserted)> TotalsAsync(string stream, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand(
            "SELECT coalesce(sum(received), 0)::bigint, coalesce(sum(inserted), 0)::bigint FROM ingest_totals WHERE stream = @stream");
        cmd.Parameters.AddWithValue("stream", stream);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    public async Task<IReadOnlyList<(DateTimeOffset Bucket, long Readings)>> ChartAsync(int minutes, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            SELECT date_bin('5 seconds', device_time, timestamptz '2000-01-01') AS bucket, count(*)
            FROM readings
            WHERE device_time >= now() - make_interval(mins => @minutes)
            GROUP BY bucket ORDER BY bucket
            """);
        cmd.Parameters.AddWithValue("minutes", minutes);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var buckets = new List<(DateTimeOffset, long)>();
        while (await reader.ReadAsync(ct)) buckets.Add((Utc(reader.GetDateTime(0)), reader.GetInt64(1)));
        return buckets;
    }

    public async Task<IReadOnlyList<AlarmView>> RecentAlarmsAsync(int limit, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            SELECT machine_id, alarm_seq, kind, severity, device_time, stored_at,
                   extract(epoch FROM stored_at - device_time)::double precision
            FROM alarms ORDER BY stored_at DESC, machine_id, alarm_seq LIMIT @limit
            """);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var alarms = new List<AlarmView>();
        while (await reader.ReadAsync(ct))
            alarms.Add(new AlarmView(reader.GetString(0), reader.GetInt64(1), Enum.Parse<AlarmKind>(reader.GetString(2)),
                reader.GetString(3), Utc(reader.GetDateTime(4)), Utc(reader.GetDateTime(5)), reader.GetDouble(6)));
        return alarms;
    }

    public async Task<long> DuplicateRowsAsync(CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("SELECT count(*) - count(DISTINCT (machine_id, seq)) FROM readings");
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
