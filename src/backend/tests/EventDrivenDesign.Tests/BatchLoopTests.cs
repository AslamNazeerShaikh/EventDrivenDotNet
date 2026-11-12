using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using Confluent.Kafka;
using EventDrivenDesign.Consumer;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EventDrivenDesign.Tests;

/// <summary>An IConsumer stand-in: serves scripted records, then nothing, and logs every StoreOffset into the shared event list.</summary>
public class FakeConsumer : DispatchProxy
{
    public ConcurrentQueue<ConsumeResult<string, byte[]>> Script { get; } = new();
    public ConcurrentQueue<string> Events { get; set; } = new();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "Consume":
                if (Script.TryDequeue(out var record))
                    return record;
                Thread.Sleep(
                    (TimeSpan)args![0]! is var t && t > TimeSpan.FromMilliseconds(5) ? 5 : 0
                );
                return null;
            case "StoreOffset" when args![0] is ConsumeResult<string, byte[]> r:
                Events.Enqueue($"store {r.Partition.Value}@{r.Offset.Value}");
                return null;
            case "get_Assignment":
                return new List<TopicPartition>();
            default:
                return method.ReturnType.IsValueType && method.ReturnType != typeof(void)
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
        }
    }
}

public sealed class BatchLoopTests
{
    private const string ReadingsTopic = "plant1.machine.readings";
    private const string AlarmsTopic = "plant1.machine.alarms";
    private const string GoodReading =
        """{"machineId":"m1","machineType":"press","shop":"PRS","seq":1,"deviceTime":"2026-01-01T00:00:00Z","signals":{"temp_c":20.5}}""";
    private const string GoodAlarm =
        """{"machineId":"m1","alarmSeq":1,"readingSeq":1,"deviceTime":"2026-01-01T00:00:00Z","kind":"Overheat","severity":"critical","signal":"temp_c","value":99,"threshold":90}""";

    private static ConsumeResult<string, byte[]> Rec(
        string topic,
        int partition,
        long offset,
        string json
    ) =>
        new()
        {
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = "k",
                Value = Encoding.UTF8.GetBytes(json),
            },
        };

    private static (FakeConsumer Fake, IConsumer<string, byte[]> Consumer) NewConsumer(
        ConcurrentQueue<string> events
    )
    {
        var consumer = DispatchProxy.Create<IConsumer<string, byte[]>, FakeConsumer>();
        var fake = (FakeConsumer)(object)consumer;
        fake.Events = events;
        return (fake, consumer);
    }

    private static async Task RunUntilAsync(
        Action<CancellationToken> run,
        Func<bool> done,
        int maxSeconds = 10
    )
    {
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => run(cts.Token));
        var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
        while (!done() && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken); // let anything that should not happen, happen
        await cts.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Offsets_are_stored_only_after_the_insert_succeeds_and_a_retry_resends_the_same_records()
    {
        var events = new ConcurrentQueue<string>();
        var (fake, consumer) = NewConsumer(events);
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 10, GoodReading));
        fake.Script.Enqueue(Rec(ReadingsTopic, 1, 20, GoodReading));
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 11, GoodReading));
        var seen =
            new List<(
                IReadOnlyList<MachineReading> Batch,
                IReadOnlyList<RejectedRecord> Rejected
            )>();
        var attempts = 0;
        var loop = new BatchLoop<MachineReading>(
            consumer,
            TopicNames.CentralReadingsPattern,
            10,
            TimeSpan.FromMilliseconds(50),
            null,
            (batch, rejected, _) =>
            {
                events.Enqueue("insert");
                seen.Add((batch, rejected));
                return ++attempts < 3
                    ? Task.FromException<int>(new InvalidOperationException("db down"))
                    : Task.FromResult(batch.Count);
            },
            new ConsumerStats(TimeProvider.System),
            NullLogger.Instance
        );

        await RunUntilAsync(loop.Run, () => events.Count(e => e.StartsWith("store")) == 2);

        events.Should().Equal("insert", "insert", "insert", "store 0@11", "store 1@20");
        seen.Should().HaveCount(3);
        seen[1].Batch.Should().BeSameAs(seen[0].Batch);
        seen[2].Batch.Should().BeSameAs(seen[0].Batch);
        seen[2].Batch.Should().HaveCount(3);
    }

    [Fact]
    public async Task Nothing_is_stored_while_the_insert_keeps_failing()
    {
        var events = new ConcurrentQueue<string>();
        var (fake, consumer) = NewConsumer(events);
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 10, GoodReading));
        var loop = new BatchLoop<MachineReading>(
            consumer,
            TopicNames.CentralReadingsPattern,
            10,
            TimeSpan.FromMilliseconds(50),
            null,
            (_, _, _) =>
            {
                events.Enqueue("insert");
                return Task.FromException<int>(new InvalidOperationException("db down"));
            },
            new ConsumerStats(TimeProvider.System),
            NullLogger.Instance
        );

        await RunUntilAsync(loop.Run, () => events.Count >= 3);

        events.Should().NotBeEmpty().And.OnlyContain(e => e == "insert");
    }

    [Fact]
    public async Task An_empty_window_makes_no_insert_call()
    {
        var calls = 0;
        var (_, consumer) = NewConsumer([]);
        var loop = new BatchLoop<MachineReading>(
            consumer,
            TopicNames.CentralReadingsPattern,
            10,
            TimeSpan.FromMilliseconds(20),
            null,
            (b, _, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(b.Count);
            },
            new ConsumerStats(TimeProvider.System),
            NullLogger.Instance
        );

        await RunUntilAsync(loop.Run, () => false, maxSeconds: 1); // several empty windows pass

        calls.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_readings_are_quarantined_and_do_not_block_the_valid_ones()
    {
        var events = new ConcurrentQueue<string>();
        var (fake, consumer) = NewConsumer(events);
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 1, GoodReading));
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 2, "{ not json"));
        fake.Script.Enqueue(Rec(ReadingsTopic, 0, 3, """{"seq":1}"""));
        IReadOnlyList<MachineReading>? parsed = null;
        IReadOnlyList<RejectedRecord>? rejected = null;
        var loop = new BatchLoop<MachineReading>(
            consumer,
            TopicNames.CentralReadingsPattern,
            10,
            TimeSpan.FromMilliseconds(50),
            null,
            (b, r, _) =>
            {
                (parsed, rejected) = (b, r);
                events.Enqueue("insert");
                return Task.FromResult(b.Count);
            },
            new ConsumerStats(TimeProvider.System),
            NullLogger.Instance
        );

        await RunUntilAsync(loop.Run, () => events.Any(e => e.StartsWith("store")));

        parsed.Should().ContainSingle().Which.MachineId.Should().Be("m1");
        rejected!
            .Select(r => (r.Topic, r.Partition, r.Offset))
            .Should()
            .Equal((ReadingsTopic, 0, 2L), (ReadingsTopic, 0, 3L));
        rejected![1].Raw.Should().Equal(Encoding.UTF8.GetBytes("""{"seq":1}"""));
        rejected![1].Error.Should().NotBeNullOrWhiteSpace();
        events.Should().Equal("insert", "store 0@3");
    }

    [Fact]
    public async Task Malformed_alarms_are_quarantined_the_same_way()
    {
        var events = new ConcurrentQueue<string>();
        var (fake, consumer) = NewConsumer(events);
        fake.Script.Enqueue(Rec(AlarmsTopic, 0, 1, GoodAlarm));
        fake.Script.Enqueue(Rec(AlarmsTopic, 0, 2, "{ not json"));
        fake.Script.Enqueue(Rec(AlarmsTopic, 0, 3, """{"alarmSeq":5}"""));
        IReadOnlyList<MachineAlarm>? parsed = null;
        IReadOnlyList<RejectedRecord>? rejected = null;
        var loop = new BatchLoop<MachineAlarm>(
            consumer,
            TopicNames.CentralAlarmsPattern,
            100,
            TimeSpan.FromMilliseconds(50),
            null,
            (b, r, _) =>
            {
                (parsed, rejected) = (b, r);
                events.Enqueue("insert");
                return Task.FromResult(b.Count);
            },
            new ConsumerStats(TimeProvider.System),
            NullLogger.Instance
        );

        await RunUntilAsync(loop.Run, () => events.Any(e => e.StartsWith("store")));

        parsed.Should().ContainSingle().Which.MachineId.Should().Be("m1");
        rejected!.Select(r => r.Offset).Should().Equal(2L, 3L);
        events.Should().Equal("insert", "store 0@3");
    }
}

public sealed class RecordValidationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static MachineReading Reading(
        string id = "m1",
        string type = "press",
        string shop = "PRS",
        long seq = 1,
        Dictionary<string, double>? signals = null
    ) => new(id, type, shop, seq, T0, signals ?? []);

    private static MachineAlarm Alarm(
        string id = "m1",
        string severity = "critical",
        long seq = 1
    ) => new(id, seq, 1, T0, AlarmKind.Overheat, severity, null, null, null);

    [Fact]
    public void A_complete_reading_and_alarm_are_valid() =>
        (RecordValidation.Problem(Reading()), RecordValidation.Problem(Alarm()))
            .Should()
            .Be((null, null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a\0b")]
    public void Bad_reading_text_is_a_problem(string? bad)
    {
        RecordValidation.Problem(Reading(id: bad!)).Should().NotBeNull();
        RecordValidation.Problem(Reading(type: bad!)).Should().NotBeNull();
        RecordValidation.Problem(Reading(shop: bad!)).Should().NotBeNull();
        RecordValidation.Problem(Alarm(id: bad!)).Should().NotBeNull();
        RecordValidation.Problem(Alarm(severity: bad!)).Should().NotBeNull();
    }

    [Fact]
    public void Non_positive_seqs_and_null_signals_are_problems()
    {
        RecordValidation.Problem(Reading(seq: 0)).Should().NotBeNull();
        RecordValidation.Problem(Alarm(seq: 0)).Should().NotBeNull();
        RecordValidation.Problem(Reading() with { Signals = null! }).Should().NotBeNull();
    }
}
