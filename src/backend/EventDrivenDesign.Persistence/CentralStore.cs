using System.Text.Json;
using EventDrivenDesign.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace EventDrivenDesign.Persistence;

public sealed record RejectedRecord(string Topic, int Partition, long Offset, byte[] Raw, string Error);

/// <summary>
/// The one deduplication point (doc 02 §4.6): the (machine_id, seq) key with ON CONFLICT DO NOTHING.
/// Rows, per-machine progress, writer counters and rejected records commit in one transaction per call.
/// </summary>
public sealed class CentralStore(NpgsqlDataSource db)
{
    private const int TextArray = (int)NpgsqlDbType.Array | (int)NpgsqlDbType.Text;
    private const int BigintArray = (int)NpgsqlDbType.Array | (int)NpgsqlDbType.Bigint;
    private const int TimestampTzArray = (int)NpgsqlDbType.Array | (int)NpgsqlDbType.TimestampTz;
    private const int DoubleArray = (int)NpgsqlDbType.Array | (int)NpgsqlDbType.Double;

    private const string InsertReadings = """
        WITH ins AS (
          INSERT INTO readings (machine_id, seq, device_time, machine_type, shop, signals)
          SELECT * FROM unnest(@machine_ids::text[], @seqs::bigint[], @device_times::timestamptz[],
                               @types::text[], @shops::text[], @signals::jsonb[])
          ON CONFLICT (machine_id, seq) DO NOTHING
          RETURNING machine_id, seq, device_time
        ), progress AS (
          INSERT INTO machine_progress AS p (machine_id, stored, max_seq, max_device_time)
          SELECT machine_id, count(*), max(seq), max(device_time) FROM ins GROUP BY machine_id
          ON CONFLICT (machine_id) DO UPDATE
            SET stored          = p.stored + EXCLUDED.stored,
                max_seq         = GREATEST(p.max_seq, EXCLUDED.max_seq),
                max_device_time = GREATEST(p.max_device_time, EXCLUDED.max_device_time)
        )
        SELECT count(*) FROM ins;
        """;

    private const string InsertAlarms = """
        WITH ins AS (
          INSERT INTO alarms (machine_id, alarm_seq, reading_seq, device_time, kind, severity, signal, value, threshold)
          SELECT * FROM unnest(@machine_ids::text[], @alarm_seqs::bigint[], @reading_seqs::bigint[],
                               @device_times::timestamptz[], @kinds::text[], @severities::text[],
                               @signals::text[], @values::double precision[], @thresholds::double precision[])
          ON CONFLICT (machine_id, alarm_seq) DO NOTHING
          RETURNING 1
        )
        SELECT count(*) FROM ins;
        """;

    private const string AddTotals = """
        INSERT INTO ingest_totals AS t (stream, writer_id, received, inserted)
        VALUES (@stream, @writer, @received, @inserted)
        ON CONFLICT (stream, writer_id) DO UPDATE
          SET received = t.received + EXCLUDED.received, inserted = t.inserted + EXCLUDED.inserted;
        """;

    private const string InsertRejected = """
        INSERT INTO rejected_records (topic, kafka_partition, kafka_offset, raw, error)
        VALUES (@topic, @partition, @offset, @raw, @error)
        ON CONFLICT DO NOTHING;
        """;

    /// <summary>Returns rows inserted. An empty batch with nothing rejected is a no-op (counters stay untouched).</summary>
    public async Task<int> InsertReadingsAsync(
        IReadOnlyList<MachineReading> batch, IReadOnlyList<RejectedRecord> rejected, string writerId, CancellationToken ct)
    {
        if (batch.Count == 0 && rejected.Count == 0) return 0;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var inserted = 0;
        if (batch.Count > 0)
        {
            await using var cmd = new NpgsqlCommand(InsertReadings, conn, tx);
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("machine_ids", [.. batch.Select(r => r.MachineId)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<long[]>("seqs", [.. batch.Select(r => r.Seq)]) { NpgsqlDbType = (NpgsqlDbType)BigintArray });
            cmd.Parameters.Add(new NpgsqlParameter<DateTime[]>("device_times", [.. batch.Select(r => r.DeviceTime.UtcDateTime)]) { NpgsqlDbType = (NpgsqlDbType)TimestampTzArray });
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("types", [.. batch.Select(r => r.MachineType)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("shops", [.. batch.Select(r => r.Shop)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("signals", [.. batch.Select(r => JsonSerializer.Serialize(r.Signals, Json.Options))]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            inserted = (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        await AddTotalsAsync(conn, tx, "readings", writerId, batch.Count, inserted, ct);
        await QuarantineAsync(conn, tx, rejected, ct);
        await tx.CommitAsync(ct);
        return inserted;
    }

    /// <summary>Returns alarms inserted. An empty batch with nothing rejected is a no-op (counters stay untouched).</summary>
    public async Task<int> InsertAlarmsAsync(
        IReadOnlyList<MachineAlarm> batch, IReadOnlyList<RejectedRecord> rejected, string writerId, CancellationToken ct)
    {
        if (batch.Count == 0 && rejected.Count == 0) return 0;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var inserted = 0;
        if (batch.Count > 0)
        {
            await using var cmd = new NpgsqlCommand(InsertAlarms, conn, tx);
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("machine_ids", [.. batch.Select(a => a.MachineId)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<long[]>("alarm_seqs", [.. batch.Select(a => a.AlarmSeq)]) { NpgsqlDbType = (NpgsqlDbType)BigintArray });
            cmd.Parameters.Add(new NpgsqlParameter<long[]>("reading_seqs", [.. batch.Select(a => a.ReadingSeq)]) { NpgsqlDbType = (NpgsqlDbType)BigintArray });
            cmd.Parameters.Add(new NpgsqlParameter<DateTime[]>("device_times", [.. batch.Select(a => a.DeviceTime.UtcDateTime)]) { NpgsqlDbType = (NpgsqlDbType)TimestampTzArray });
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("kinds", [.. batch.Select(a => a.Kind.ToString())]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<string[]>("severities", [.. batch.Select(a => a.Severity)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<string?[]>("signals", [.. batch.Select(a => a.Signal)]) { NpgsqlDbType = (NpgsqlDbType)TextArray });
            cmd.Parameters.Add(new NpgsqlParameter<double?[]>("values", [.. batch.Select(a => a.Value)]) { NpgsqlDbType = (NpgsqlDbType)DoubleArray });
            cmd.Parameters.Add(new NpgsqlParameter<double?[]>("thresholds", [.. batch.Select(a => a.Threshold)]) { NpgsqlDbType = (NpgsqlDbType)DoubleArray });
            inserted = (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        await AddTotalsAsync(conn, tx, "alarms", writerId, batch.Count, inserted, ct);
        await QuarantineAsync(conn, tx, rejected, ct);
        await tx.CommitAsync(ct);
        return inserted;
    }

    private static async Task QuarantineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<RejectedRecord> rejected, CancellationToken ct)
    {
        foreach (var r in rejected)
        {
            await using var cmd = new NpgsqlCommand(InsertRejected, conn, tx);
            cmd.Parameters.AddWithValue("topic", r.Topic);
            cmd.Parameters.AddWithValue("partition", r.Partition);
            cmd.Parameters.AddWithValue("offset", r.Offset);
            cmd.Parameters.AddWithValue("raw", r.Raw);
            cmd.Parameters.AddWithValue("error", r.Error);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task AddTotalsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string stream, string writerId, int received, int inserted, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(AddTotals, conn, tx);
        cmd.Parameters.AddWithValue("stream", stream);
        cmd.Parameters.AddWithValue("writer", writerId);
        cmd.Parameters.AddWithValue("received", (long)received);
        cmd.Parameters.AddWithValue("inserted", (long)inserted);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
