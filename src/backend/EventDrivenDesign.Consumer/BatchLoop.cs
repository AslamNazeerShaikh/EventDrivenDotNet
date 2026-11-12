using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Consumer;

/// <summary>Process-wide counters for /metrics, shared by both loops.</summary>
public sealed class ConsumerStats(TimeProvider time)
{
    private long _received, _inserted, _rejected, _lastBatchMs;

    public RateMeter ConsumedRate { get; } = new(time);
    public RateMeter InsertedRate { get; } = new(time);
    public long Received => Interlocked.Read(ref _received);
    public long Inserted => Interlocked.Read(ref _inserted);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long LastBatchMs => Interlocked.Read(ref _lastBatchMs);

    public void Record(int received, int inserted, int rejected, long batchMs)
    {
        Interlocked.Add(ref _received, received);
        Interlocked.Add(ref _inserted, inserted);
        Interlocked.Add(ref _rejected, rejected);
        Interlocked.Exchange(ref _lastBatchMs, batchMs);
        ConsumedRate.Add(received);
        InsertedRate.Add(inserted);
    }
}

/// <summary>
/// One store-writer loop (doc 00 §7, doc 02 §4.4 row 3): collect a batch, insert it through the unique key, and only then
/// store offsets. A database error retries the same records, so nothing is skipped. Runs on a dedicated thread because
/// librdkafka's Consume blocks.
/// </summary>
public sealed class BatchLoop<T>(
    IConsumer<string, byte[]> consumer, string pattern, int maxCount, TimeSpan window, RateCap? cap,
    Func<IReadOnlyList<T>, IReadOnlyList<RejectedRecord>, CancellationToken, Task<int>> insert,
    ConsumerStats stats, ILogger log)
{
    private readonly Random _rng = new();

    public void Run(CancellationToken ct)
    {
        try
        {
            consumer.Subscribe(pattern);
            while (!ct.IsCancellationRequested)
            {
                var clock = Stopwatch.StartNew();
                var records = Collect(ct);
                if (records.Count == 0) continue; // an empty window makes no database call

                var (parsed, rejected) = Parse(records);
                var inserted = InsertWithRetry(parsed, rejected, ct);
                foreach (var last in LastPerPartition(records)) StoreOffset(last);
                stats.Record(records.Count, inserted, rejected.Count, clock.ElapsedMilliseconds);

                if (cap is not null) ct.WaitHandle.WaitOne(cap.DelayAfter(records.Count, clock.Elapsed));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close(); // commits the stored offsets and leaves the group
            consumer.Dispose();
        }
    }

    public int[] Assignment => [.. consumer.Assignment.Select(p => p.Partition.Value)];

    /// <summary>The offset to store is the last record of each partition: StoreOffset keeps offset + 1.</summary>
    public static IEnumerable<ConsumeResult<string, byte[]>> LastPerPartition(IEnumerable<ConsumeResult<string, byte[]>> batch) =>
        batch.GroupBy(r => r.TopicPartition).Select(g => g.Last());

    /// <summary>Revoke handler: commit what was stored. A failure is logged because the redelivery is absorbed by the key.</summary>
    public static void CommitOnRevoke(IConsumer<string, byte[]> c, ILogger log)
    {
        try { c.Commit(); }
        catch (KafkaException ex) { log.LogWarning("Commit during revoke failed: {Reason}", ex.Error.Reason); }
    }

    private List<ConsumeResult<string, byte[]>> Collect(CancellationToken ct)
    {
        var records = new List<ConsumeResult<string, byte[]>>();
        var deadline = Stopwatch.StartNew();
        while (records.Count < maxCount && deadline.Elapsed < window && !ct.IsCancellationRequested)
        {
            try
            {
                var record = consumer.Consume(window - deadline.Elapsed);
                if (record is not null) records.Add(record);
            }
            catch (ConsumeException ex) when (!ex.Error.IsFatal)
            {
                log.LogWarning("Consume error: {Reason}", ex.Error.Reason);
            }
        }
        return records;
    }

    private (List<T> Parsed, List<RejectedRecord> Rejected) Parse(List<ConsumeResult<string, byte[]>> records)
    {
        var parsed = new List<T>(records.Count);
        var rejected = new List<RejectedRecord>();
        foreach (var r in records)
        {
            try
            {
                parsed.Add(JsonSerializer.Deserialize<T>(r.Message.Value, Json.Options) ?? throw new JsonException("null payload"));
            }
            catch (JsonException ex)
            {
                log.LogWarning("Rejected {Topic}[{Partition}]@{Offset}: {Error}", r.Topic, r.Partition.Value, r.Offset.Value, ex.Message);
                rejected.Add(new RejectedRecord(r.Topic, r.Partition.Value, r.Offset.Value, r.Message.Value ?? [], ex.Message));
            }
        }
        return (parsed, rejected);
    }

    private int InsertWithRetry(List<T> parsed, List<RejectedRecord> rejected, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return insert(parsed, rejected, ct).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var delay = Retry.Backoff(attempt, _rng);
                log.LogWarning(ex, "Insert failed (attempt {Attempt}); retrying the same batch in {Delay}", attempt + 1, delay);
                ct.WaitHandle.WaitOne(delay);
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    private void StoreOffset(ConsumeResult<string, byte[]> last)
    {
        // A partition revoked mid-batch is no longer ours to store; its new owner redelivers and the key absorbs it.
        try { consumer.StoreOffset(last); }
        catch (KafkaException ex) { log.LogWarning("StoreOffset {Partition} skipped: {Reason}", last.TopicPartition, ex.Error.Reason); }
    }
}
