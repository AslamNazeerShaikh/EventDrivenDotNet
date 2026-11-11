namespace EventDrivenDesign.Persistence;

/// <summary>
/// SQLite sink schema (one file per consumer) from spec section 5:
/// WAL mode, one writer per file, idempotent inserts on <c>event_id</c>.
/// </summary>
public static class SinkSchema
{
    /// <summary>Pragmas applied at every connection open.</summary>
    public const string Pragmas = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA busy_timeout = 5000;
        """;

    /// <summary>Creates the events table and its indexes idempotently.</summary>
    public const string CreateScript = """
        CREATE TABLE IF NOT EXISTS events (
          event_id        TEXT    PRIMARY KEY,
          producer_id     TEXT    NOT NULL,
          run_id          TEXT    NOT NULL,
          stream_id       TEXT    NOT NULL,
          seq             INTEGER NOT NULL,
          event_type      TEXT    NOT NULL,
          payload         TEXT    NOT NULL,
          kafka_partition INTEGER NOT NULL,
          kafka_offset    INTEGER NOT NULL,
          produced_at_ms  INTEGER NOT NULL,
          consumed_at_ms  INTEGER NOT NULL,
          persisted_at_ms INTEGER NOT NULL,
          sink_id         TEXT    NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_stream_seq ON events(stream_id, seq);
        CREATE INDEX IF NOT EXISTS ix_persisted ON events(persisted_at_ms);
        """;

    /// <summary>
    /// Gap check per stream over both sink files (attached as <c>main</c> and <c>b</c>).
    /// <c>missing</c> is 0 for every stream once lag reaches 0.
    /// </summary>
    public const string GapQuerySql = """
        SELECT producer_id, stream_id,
               COUNT(*)            AS rows_persisted,
               MAX(seq)            AS max_seq,
               MAX(seq) - COUNT(*) AS missing
        FROM (SELECT producer_id, stream_id, seq FROM main.events
              UNION ALL
              SELECT producer_id, stream_id, seq FROM b.events)
        GROUP BY producer_id, stream_id;
        """;
}

/// <summary>
/// SQLite outbox schema for producers: events are stored in the same
/// transaction as the run state, then a relay publishes pending rows.
/// </summary>
public static class OutboxSchema
{
    /// <summary>Creates the outbox table idempotently.</summary>
    public const string CreateScript = """
        CREATE TABLE IF NOT EXISTS outbox (
          outbox_id     INTEGER PRIMARY KEY AUTOINCREMENT,
          topic         TEXT    NOT NULL,
          message_key   TEXT    NOT NULL,
          payload       TEXT    NOT NULL,
          stream_id     TEXT    NOT NULL,
          seq           INTEGER NOT NULL,
          created_at_ms INTEGER NOT NULL,
          dispatched_at_ms INTEGER NULL
        );
        CREATE INDEX IF NOT EXISTS ix_outbox_pending ON outbox(dispatched_at_ms, outbox_id);
        """;
}
