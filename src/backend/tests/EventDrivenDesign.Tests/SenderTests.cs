using System.Text;
using AwesomeAssertions;
using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EventDrivenDesign.Tests;

/// <summary>The sender's failure and state paths against a stub producer: no Docker, no broker.</summary>
public sealed class SenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sender").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private GatewayBuffer OpenBuffer() => GatewayBuffer.Open(Path.Combine(_dir, "buffer.db"));

    private static async Task Fill(GatewayBuffer buffer, int count, long deviceTimeMs, Lane lane = Lane.Reading)
    {
        var rows = Enumerable.Range(1, count)
            .Select(i => new OutboxRow(0, lane, "P1-BDY-W001", i, deviceTimeMs, [1, 2, 3])).ToList();
        await buffer.WriteTickAsync(new TickBatch(rows, []), TestContext.Current.CancellationToken);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met within 30 s");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static long Remaining(GatewayBuffer buffer) => buffer.Stats() is var s ? s.Readings + s.Alarms : 0;

    [Fact]
    public async Task Partial_batch_deletes_only_acknowledged_rows_then_backs_off_and_retries()
    {
        await using var buffer = OpenBuffer();
        await Fill(buffer, 5, _time.GetUtcNow().ToUnixTimeMilliseconds());
        var producer = new StubProducer { Decide = (_, m) => Encoding.UTF8.GetString(m.Headers.GetLastBytes("ce_id")) is "P1-BDY-W001-3" or "P1-BDY-W001-4" ? Outcome.Fail : Outcome.Ack };
        var sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => sender.State == SenderState.Retrying);
        sender.Sent.Should().Be(3);
        sender.SendErrors.Should().Be(2);
        buffer.ReadOldest(10).Select(r => r.Seq).Should().Equal(3, 4);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        producer.Produced.Should().Be(5, "the sender waits out its backoff instead of retrying at once");

        _time.Advance(TimeSpan.FromSeconds(31)); // beyond the 30 s backoff cap
        await Until(() => sender.SendErrors >= 4);
        producer.Produced.Should().Be(7, "only the two failed rows are produced again");
        buffer.ReadOldest(10).Select(r => r.Seq).Should().Equal(3, 4);

        await cts.CancelAsync();
        await run;
        sender.State.Should().Be(SenderState.Stopping);
    }

    [Fact]
    public async Task Synchronous_produce_exception_leaves_that_row_and_does_not_hang_the_batch()
    {
        await using var buffer = OpenBuffer();
        await Fill(buffer, 5, _time.GetUtcNow().ToUnixTimeMilliseconds());
        var producer = new StubProducer { Decide = (i, _) => i == 2 ? Outcome.Throw : Outcome.Ack };
        var sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => sender.State == SenderState.Retrying);
        sender.Sent.Should().Be(4);
        sender.SendErrors.Should().Be(1);
        buffer.ReadOldest(10).Should().ContainSingle().Which.Seq.Should().Be(3);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Shutdown_flushes_and_deletes_only_what_was_acknowledged()
    {
        await using var buffer = OpenBuffer();
        await Fill(buffer, 5, _time.GetUtcNow().ToUnixTimeMilliseconds());
        // Rows 0-2 are acknowledged at once, row 3 only when the shutdown flush delivers it, row 4 never.
        var producer = new StubProducer { Decide = (i, _) => i < 3 ? Outcome.Ack : Outcome.Hold, ReleaseOnFlush = 1 };
        var sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => producer.Produced == 5);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Remaining(buffer).Should().Be(5, "nothing is deleted until the whole batch has reported");

        await cts.CancelAsync();
        await run;
        producer.FlushCalls.Should().BeGreaterThanOrEqualTo(1);
        sender.State.Should().Be(SenderState.Stopping);
        sender.Sent.Should().Be(4);
        buffer.ReadOldest(10).Should().ContainSingle().Which.Seq.Should().Be(5);
    }

    [Fact]
    public async Task Old_backlog_switches_to_catching_up_and_reads_5000_at_a_time()
    {
        await using var buffer = OpenBuffer();
        await Fill(buffer, 7000, _time.GetUtcNow().AddSeconds(-10).ToUnixTimeMilliseconds());
        var remainingAt = new Dictionary<int, long>();
        var stateAt = new Dictionary<int, SenderState>();
        Sender? sender = null;
        var producer = new StubProducer
        {
            Decide = (i, _) =>
            {
                if (i is 0 or 999 or 1000 or 5999 or 6000)
                {
                    remainingAt[i] = Remaining(buffer);
                    stateAt[i] = sender!.State;
                }
                return Outcome.Ack;
            },
        };
        sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => Remaining(buffer) == 0);
        await cts.CancelAsync();
        await run;

        remainingAt[0].Should().Be(7000);
        remainingAt[999].Should().Be(7000, "the first read is the live batch of 1,000");
        stateAt[999].Should().Be(SenderState.Starting);
        remainingAt[1000].Should().Be(6000);
        stateAt[1000].Should().Be(SenderState.CatchingUp, "the oldest row is over 5 s old");
        remainingAt[5999].Should().Be(6000, "5,000 rows are produced from one read before any delete");
        remainingAt[6000].Should().Be(1000);
        sender.Sent.Should().Be(7000);
    }

    [Fact]
    public async Task Fresh_rows_stay_in_sending_with_the_live_batch_size()
    {
        await using var buffer = OpenBuffer();
        await Fill(buffer, 1500, _time.GetUtcNow().ToUnixTimeMilliseconds());
        var remainingAt = new Dictionary<int, long>();
        var stateAt = new Dictionary<int, SenderState>();
        Sender? sender = null;
        var producer = new StubProducer
        {
            Decide = (i, _) =>
            {
                if (i is 1000 or 1499)
                {
                    remainingAt[i] = Remaining(buffer);
                    stateAt[i] = sender!.State;
                }
                return Outcome.Ack;
            },
        };
        sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => Remaining(buffer) == 0);
        await Until(() => sender.State == SenderState.Sending);
        await cts.CancelAsync();
        await run;

        stateAt[1000].Should().Be(SenderState.Sending);
        remainingAt[1000].Should().Be(500);
        remainingAt[1499].Should().Be(500);
    }

    [Fact]
    public async Task Rows_go_to_their_lane_topic_with_key_device_time_and_headers()
    {
        await using var buffer = OpenBuffer();
        var t = _time.GetUtcNow().ToUnixTimeMilliseconds();
        await Fill(buffer, 1, t, Lane.Alarm);
        await Fill(buffer, 1, t);
        var producer = new StubProducer { Decide = (_, _) => Outcome.Ack };
        var sender = new Sender(buffer, producer, "BDY", _time);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await Until(() => Remaining(buffer) == 0);
        await cts.CancelAsync();
        await run;

        producer.Messages.Select(m => m.Topic).Should().Equal(TopicNames.Alarms, TopicNames.Readings);
        producer.Messages.Should().OnlyContain(m => m.Message.Key == "P1-BDY-W001" && m.Message.Timestamp.UnixTimestampMs == t);
        producer.Messages[0].Message.Headers.GetLastBytes("ce_id").Should().Equal("P1-BDY-W001-a1"u8.ToArray());
        producer.Messages[1].Message.Headers.GetLastBytes("ce_id").Should().Equal("P1-BDY-W001-1"u8.ToArray());
    }
}

internal enum Outcome { Ack, Fail, Throw, Hold }

/// <summary>Only what Sender calls is real: Produce(topic, message, handler) and Flush(timeout).</summary>
internal sealed class StubProducer : IProducer<string, byte[]>
{
    private readonly List<Action> _held = [];
    private int _produced;

    public Func<int, Message<string, byte[]>, Outcome> Decide { get; init; } = (_, _) => Outcome.Ack;
    /// <summary>How many held reports a Flush delivers; the rest stay unacknowledged.</summary>
    public int ReleaseOnFlush { get; init; }
    public int FlushCalls { get; private set; }
    public int Produced => Volatile.Read(ref _produced);
    public List<(string Topic, Message<string, byte[]> Message)> Messages { get; } = [];

    public void Produce(string topic, Message<string, byte[]> message, Action<DeliveryReport<string, byte[]>>? deliveryHandler = null)
    {
        var index = Produced;
        Messages.Add((topic, message));
        var outcome = Decide(index, message);
        Volatile.Write(ref _produced, index + 1);
        DeliveryReport<string, byte[]> Report(Error error, PersistenceStatus status) =>
            new() { Topic = topic, Message = message, Error = error, Status = status };
        switch (outcome)
        {
            case Outcome.Ack:
                deliveryHandler!(Report(new Error(ErrorCode.NoError), PersistenceStatus.Persisted));
                break;
            case Outcome.Fail:
                deliveryHandler!(Report(new Error(ErrorCode.Local_MsgTimedOut), PersistenceStatus.NotPersisted));
                break;
            case Outcome.Hold:
                lock (_held) _held.Add(() => deliveryHandler!(Report(new Error(ErrorCode.NoError), PersistenceStatus.Persisted)));
                break;
            default:
                throw new ProduceException<string, byte[]>(new Error(ErrorCode.Local_QueueFull), Report(new Error(ErrorCode.Local_QueueFull), PersistenceStatus.NotPersisted));
        }
    }

    public int Flush(TimeSpan timeout)
    {
        FlushCalls++;
        lock (_held)
        {
            foreach (var release in _held.Take(ReleaseOnFlush)) release();
            _held.RemoveRange(0, Math.Min(ReleaseOnFlush, _held.Count));
            return _held.Count;
        }
    }

    // Not used by Sender.
    public Handle Handle => throw new NotSupportedException();
    public string Name => "stub";
    public int AddBrokers(string brokers) => throw new NotSupportedException();
    public void SetSaslCredentials(string username, string password) => throw new NotSupportedException();
    public void Dispose() { }
    public void Produce(TopicPartition topicPartition, Message<string, byte[]> message, Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) => throw new NotSupportedException();
    public Task<DeliveryResult<string, byte[]>> ProduceAsync(string topic, Message<string, byte[]> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DeliveryResult<string, byte[]>> ProduceAsync(TopicPartition topicPartition, Message<string, byte[]> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public int Poll(TimeSpan timeout) => throw new NotSupportedException();
    public void Flush(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void InitTransactions(TimeSpan timeout) => throw new NotSupportedException();
    public void BeginTransaction() => throw new NotSupportedException();
    public void CommitTransaction(TimeSpan timeout) => throw new NotSupportedException();
    public void CommitTransaction() => throw new NotSupportedException();
    public void AbortTransaction(TimeSpan timeout) => throw new NotSupportedException();
    public void AbortTransaction() => throw new NotSupportedException();
    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) => throw new NotSupportedException();
}
