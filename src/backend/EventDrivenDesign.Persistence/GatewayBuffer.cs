using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace EventDrivenDesign.Persistence;

public enum Lane { Alarm = 0, Reading = 1 }

public sealed record OutboxRow(long Id, Lane Lane, string MachineId, long Seq, long DeviceTimeMs, byte[] Payload);

public sealed record MachineState(string MachineId, long LastSeq, long LastAlarmSeq, long LastDeviceTimeMs);

public sealed record TickBatch(IReadOnlyList<OutboxRow> Rows, IReadOnlyList<MachineState> States);

public sealed record BufferStats(long Readings, long Alarms, long? OldestDeviceTimeMs, long FileBytes);

/// <summary>
/// SQLite store-and-forward buffer (doc 02 §4.2). One dedicated thread owns the only write connection;
/// every write is queued to it and committed as one transaction. Reads use short-lived connections.
/// </summary>
public sealed class GatewayBuffer : IAsyncDisposable
{
    private sealed record WriteOp(Action<SqliteConnection> Run, TaskCompletionSource Done);

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS outbox (
          id             INTEGER PRIMARY KEY,
          lane           INTEGER NOT NULL,
          machine_id     TEXT    NOT NULL,
          seq            INTEGER NOT NULL,
          device_time_ms INTEGER NOT NULL,
          payload        BLOB    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_outbox_lane ON outbox (lane, id);
        CREATE TABLE IF NOT EXISTS machine_state (
          machine_id          TEXT    PRIMARY KEY,
          last_seq            INTEGER NOT NULL,
          last_alarm_seq      INTEGER NOT NULL,
          last_device_time_ms INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS gateway_state (
          id     INTEGER PRIMARY KEY CHECK (id = 1),
          paused INTEGER NOT NULL
        );
        """;

    private readonly string _path;
    private readonly string _connectionString;
    private readonly SqliteConnection _writer;
    private readonly Channel<WriteOp> _queue =
        Channel.CreateBounded<WriteOp>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Thread _thread;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    private GatewayBuffer(string path)
    {
        _path = path;
        // Pooling off: disposing a connection really closes the file, so reopening the same path is clean.
        _connectionString = $"Data Source={path};Pooling=False";
        _writer = new SqliteConnection(_connectionString);
        _writer.Open();
        // auto_vacuum only takes effect before the first table exists; synchronous is per connection.
        Exec(_writer, "PRAGMA auto_vacuum = INCREMENTAL");
        Exec(_writer, "PRAGMA journal_mode = WAL");
        Exec(_writer, "PRAGMA synchronous = FULL");
        Exec(_writer, Schema);
        IsNew = Scalar<long>("SELECT COUNT(*) FROM machine_state") == 0;
        _thread = new Thread(WriterLoop) { IsBackground = true, Name = "gateway-buffer-writer" };
        _thread.Start();
    }

    public static GatewayBuffer Open(string path) => new(path);

    /// <summary>True when machine_state was empty at open.</summary>
    public bool IsNew { get; }

    public IReadOnlyDictionary<string, MachineState> LoadState()
    {
        using var conn = OpenReader();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT machine_id, last_seq, last_alarm_seq, last_device_time_ms FROM machine_state";
        using var r = cmd.ExecuteReader();
        var result = new Dictionary<string, MachineState>();
        while (r.Read())
            result[r.GetString(0)] = new MachineState(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
        return result;
    }

    public bool LoadPaused() => Scalar<long?>("SELECT paused FROM gateway_state WHERE id = 1") == 1;

    public Task WriteTickAsync(TickBatch tick, CancellationToken ct) => Submit(conn =>
    {
        using var tx = conn.BeginTransaction();
        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            "INSERT INTO outbox (lane, machine_id, seq, device_time_ms, payload) VALUES ($l, $m, $s, $t, $p)";
        var l = insert.Parameters.Add("$l", SqliteType.Integer);
        var m = insert.Parameters.Add("$m", SqliteType.Text);
        var s = insert.Parameters.Add("$s", SqliteType.Integer);
        var t = insert.Parameters.Add("$t", SqliteType.Integer);
        var p = insert.Parameters.Add("$p", SqliteType.Blob);
        foreach (var row in tick.Rows)
        {
            l.Value = (long)row.Lane;
            m.Value = row.MachineId;
            s.Value = row.Seq;
            t.Value = row.DeviceTimeMs;
            p.Value = row.Payload;
            insert.ExecuteNonQuery();
        }

        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = """
            INSERT INTO machine_state (machine_id, last_seq, last_alarm_seq, last_device_time_ms)
            VALUES ($m, $s, $a, $t)
            ON CONFLICT (machine_id) DO UPDATE SET
              last_seq = excluded.last_seq,
              last_alarm_seq = excluded.last_alarm_seq,
              last_device_time_ms = excluded.last_device_time_ms
            """;
        var um = upsert.Parameters.Add("$m", SqliteType.Text);
        var us = upsert.Parameters.Add("$s", SqliteType.Integer);
        var ua = upsert.Parameters.Add("$a", SqliteType.Integer);
        var ut = upsert.Parameters.Add("$t", SqliteType.Integer);
        foreach (var state in tick.States)
        {
            um.Value = state.MachineId;
            us.Value = state.LastSeq;
            ua.Value = state.LastAlarmSeq;
            ut.Value = state.LastDeviceTimeMs;
            upsert.ExecuteNonQuery();
        }

        tx.Commit();
    }, ct);

    public Task DeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => Submit(conn =>
    {
        using (var tx = conn.BeginTransaction())
        {
            using var delete = conn.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM outbox WHERE id = $id";
            var id = delete.Parameters.Add("$id", SqliteType.Integer);
            foreach (var value in ids)
            {
                id.Value = value;
                delete.ExecuteNonQuery();
            }

            tx.Commit();
        }

        Exec(conn, "PRAGMA incremental_vacuum(256)");
    }, ct);

    public Task SetPausedAsync(bool paused, CancellationToken ct) => Submit(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO gateway_state (id, paused) VALUES (1, $p)
            ON CONFLICT (id) DO UPDATE SET paused = excluded.paused
            """;
        cmd.Parameters.AddWithValue("$p", paused ? 1L : 0L);
        cmd.ExecuteNonQuery();
    }, ct);

    public IReadOnlyList<OutboxRow> ReadOldest(int limit)
    {
        using var conn = OpenReader();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, lane, machine_id, seq, device_time_ms, payload FROM outbox ORDER BY lane, id LIMIT $n";
        cmd.Parameters.AddWithValue("$n", (long)limit);
        using var r = cmd.ExecuteReader();
        var rows = new List<OutboxRow>();
        while (r.Read())
            rows.Add(new OutboxRow(r.GetInt64(0), (Lane)r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4),
                (byte[])r[5]));
        return rows;
    }

    public BufferStats Stats()
    {
        using var conn = OpenReader();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT COUNT(*) FROM outbox WHERE lane = 1),
                   (SELECT COUNT(*) FROM outbox WHERE lane = 0),
                   (SELECT MIN(device_time_ms) FROM outbox)
            """;
        using var r = cmd.ExecuteReader();
        r.Read();
        var oldest = r.IsDBNull(2) ? (long?)null : r.GetInt64(2);
        return new BufferStats(r.GetInt64(0), r.GetInt64(1), oldest, FileLength(_path) + FileLength(_path + "-wal"));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _queue.Writer.Complete();
        await _stopped.Task; // the writer drains queued operations before it exits
        _writer.Dispose();
    }

    private Task Submit(Action<SqliteConnection> run, CancellationToken ct)
    {
        var op = new WriteOp(run, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        return Enqueue(op, ct);
    }

    private async Task Enqueue(WriteOp op, CancellationToken ct)
    {
        await _queue.Writer.WriteAsync(op, ct);
        await op.Done.Task; // once queued the operation runs to completion even if ct fires
    }

    private void WriterLoop()
    {
        try
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var op))
                {
                    try
                    {
                        op.Run(_writer);
                        op.Done.SetResult();
                    }
                    catch (Exception ex)
                    {
                        op.Done.SetException(ex);
                    }
                }
            }
        }
        finally
        {
            _stopped.SetResult();
        }
    }

    private SqliteConnection OpenReader()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private T Scalar<T>(string sql)
    {
        using var conn = OpenReader();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
