using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Gateway;

public enum SenderState { Starting, Sending, CatchingUp, Retrying, Stopping }

/// <summary>
/// The relay half of the transactional outbox (doc 02 §4.5): produce buffered rows, wait for the broker's
/// acknowledgement, and delete only the rows that were acknowledged. A produce failure is counted, the row stays
/// in the buffer, and the loop backs off and reads it again. Buffer exceptions propagate (the host treats them as fatal).
/// </summary>
public sealed class Sender(GatewayBuffer buffer, IProducer<string, byte[]> producer, string shop, TimeProvider time)
{
    private const int LiveBatch = 1_000;
    private const int CatchUpBatch = 5_000;
    private static readonly TimeSpan CatchUpAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(10);

    private readonly Random _rng = new();
    private readonly Queue<(DateTimeOffset At, int Count)> _recent = new();
    private int _state = (int)SenderState.Starting;
    private long _sent;
    private long _sendErrors;

    public SenderState State => (SenderState)Volatile.Read(ref _state);
    public long Sent => Interlocked.Read(ref _sent);
    public long SendErrors => Interlocked.Read(ref _sendErrors);

    /// <summary>Rows acknowledged per second over the last 10 s.</summary>
    public double SentPerSecond
    {
        get
        {
            lock (_recent)
            {
                Prune();
                return _recent.Sum(r => r.Count) / RateWindow.TotalSeconds;
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var rows = buffer.ReadOldest(State == SenderState.CatchingUp ? CatchUpBatch : LiveBatch);
                if (rows.Count == 0)
                {
                    SetState(SenderState.Sending);
                    await Task.Delay(IdleWait, time, ct);
                    continue;
                }

                if (await SendAsync(rows, ct))
                {
                    attempt = 0;
                    var oldest = buffer.Stats().OldestDeviceTimeMs;
                    var behind = oldest is { } ms && time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(ms) > CatchUpAge;
                    SetState(behind ? SenderState.CatchingUp : SenderState.Sending);
                }
                else if (!ct.IsCancellationRequested)
                {
                    SetState(SenderState.Retrying);
                    await Task.Delay(Retry.Backoff(attempt++, _rng), time, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        SetState(SenderState.Stopping);
        producer.Flush(FlushWait); // in-flight reports are not abandoned; whatever stays unacknowledged stays on disk
    }

    /// <summary>Produces the batch, waits for every delivery report and deletes the acknowledged ids. True when all were acknowledged.</summary>
    private async Task<bool> SendAsync(IReadOnlyList<OutboxRow> rows, CancellationToken ct)
    {
        var acked = new int[rows.Count];
        var pending = rows.Count;
        var failed = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Report(int i, bool ok)
        {
            if (ok) Volatile.Write(ref acked[i], 1);
            else
            {
                Interlocked.Increment(ref failed);
                Interlocked.Increment(ref _sendErrors);
            }
            if (Interlocked.Decrement(ref pending) == 0) done.SetResult();
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var index = i;
            var isAlarm = row.Lane == Lane.Alarm;
            var message = new Message<string, byte[]>
            {
                Key = row.MachineId,
                Value = row.Payload,
                Timestamp = new Timestamp(row.DeviceTimeMs, TimestampType.CreateTime),
                Headers = CloudEvents.For(isAlarm, shop, row.MachineId, row.Seq, row.DeviceTimeMs),
            };
            try
            {
                producer.Produce(isAlarm ? TopicNames.Alarms : TopicNames.Readings, message,
                    report => Report(index, !report.Error.IsError && report.Status == PersistenceStatus.Persisted));
            }
            catch (ProduceException<string, byte[]>) // queue full or message too large: the row stays buffered
            {
                Report(index, false);
            }
        }

        try
        {
            await done.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetState(SenderState.Stopping);
            producer.Flush(FlushWait);
        }

        var ids = new List<long>();
        for (var i = 0; i < rows.Count; i++)
            if (Volatile.Read(ref acked[i]) == 1) ids.Add(rows[i].Id);
        if (ids.Count > 0)
        {
            await buffer.DeleteAsync(ids, CancellationToken.None); // acknowledged rows are deleted even during shutdown
            Interlocked.Add(ref _sent, ids.Count);
            lock (_recent)
            {
                _recent.Enqueue((time.GetUtcNow(), ids.Count));
                Prune();
            }
        }

        return ids.Count == rows.Count;
    }

    private void Prune()
    {
        var cutoff = time.GetUtcNow() - RateWindow;
        while (_recent.TryPeek(out var r) && r.At < cutoff) _recent.Dequeue();
    }

    private void SetState(SenderState state) => Volatile.Write(ref _state, (int)state);
}
