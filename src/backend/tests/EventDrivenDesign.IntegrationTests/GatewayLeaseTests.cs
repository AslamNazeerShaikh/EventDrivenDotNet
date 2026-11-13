using System.Text.Json;
using AwesomeAssertions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;
using Testcontainers.Kafka;
using Xunit;

namespace EventDrivenDesign.IntegrationTests;

public sealed class GatewayLeaseTests : IAsyncLifetime
{
    private static readonly string[] Machines = ["P1-TST-W001", "P1-TST-W002"];

    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();
    private readonly string _dir = Directory
        .CreateTempSubdirectory("gateway-lease-tests-")
        .FullName;
    private readonly List<GatewayLease> _leases = [];

    public async ValueTask InitializeAsync()
    {
        await _kafka.StartAsync(TestContext.Current.CancellationToken);
        using var admin = new AdminClientBuilder(
            KafkaConfigs.Admin(_kafka.GetBootstrapAddress())
        ).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = TopicNames.Readings,
                NumPartitions = 12,
                ReplicationFactor = 1,
            },
            new TopicSpecification
            {
                Name = TopicNames.Alarms,
                NumPartitions = 3,
                ReplicationFactor = 1,
            },
            new TopicSpecification
            {
                Name = TopicNames.Lease("TST"),
                NumPartitions = 1,
                ReplicationFactor = 1,
            },
        ]);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lease in _leases)
            try
            {
                await lease.StopAsync(CancellationToken.None);
                lease.Dispose();
            }
            catch (Exception)
            {
                // A lease that already stopped itself has nothing to tell us.
            }
        await _kafka.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_joining_instance_stays_the_standby()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-b.db"));
        await using var aBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-a.db"));
        var bHarness = new LeaseHarness();
        var aHarness = new LeaseHarness();
        var b = Track(NewLease("tst-b", bBuffer, bHarness));
        await b.StartAsync(CancellationToken.None);
        (await EventuallyAsync(() => b.IsActive, TimeSpan.FromSeconds(15)))
            .Should()
            .BeTrue("the first member is granted the lease");

        var a = Track(NewLease("tst-a", aBuffer, aHarness));
        await a.StartAsync(CancellationToken.None);
        // Within 15 s exactly one is active and it is the incumbent: cooperative-sticky keeps the current owner.
        var settled = await EventuallyAsync(
            () => b.IsActive && !a.IsActive && aHarness.Starts == 0,
            TimeSpan.FromSeconds(15)
        );
        settled.Should().BeTrue();
        b.IsActive.Should().BeTrue();
        a.IsActive.Should().BeFalse();
        // Sampled every 100 ms for the next 5 s, never both, and the incumbent keeps the lease.
        for (var i = 0; i < 50; i++)
        {
            (a.IsActive && b.IsActive).Should().BeFalse();
            b.IsActive.Should()
                .BeTrue("an empty assignment to the incumbent must not stop its term");
            await Task.Delay(100, ct);
        }
    }

    [Fact]
    public async Task The_standby_takes_over_within_15_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-b.db"));
        await using var aBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-a.db"));
        var bHarness = new LeaseHarness();
        var aHarness = new LeaseHarness();
        var b = Track(NewLease("tst-b", bBuffer, bHarness));
        var a = Track(NewLease("tst-a", aBuffer, aHarness));
        await b.StartAsync(CancellationToken.None);
        await a.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && !b.IsActive && !a.IsActive)
            await Task.Delay(100, ct);
        var active = b.IsActive ? b : a;
        var standby = b.IsActive ? a : b;
        var harness = b.IsActive ? bHarness : aHarness;
        harness.Starts.Should().BeGreaterThan(0);

        await active.StopAsync(CancellationToken.None);

        // A graceful stop cancels the term token at once, inside the host stop.
        harness.TermCancelled.Task.IsCompleted.Should().BeTrue();
        // The static member sends no LeaveGroup, so the standby is granted the lease after the 6 s session timeout.
        var tookOver = await EventuallyAsync(() => standby.IsActive, TimeSpan.FromSeconds(15));
        tookOver.Should().BeTrue();
        standby.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_standby_reconciles_its_buffer()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrap = _kafka.GetBootstrapAddress();
        await using var bBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-b.db"));
        // Two buffered rows whose seqs the log already holds.
        var t0 = DateTimeOffset.UtcNow;
        var ms1 = t0.AddSeconds(-3).ToUnixTimeMilliseconds();
        var ms2 = t0.AddSeconds(-2).ToUnixTimeMilliseconds();
        OutboxRow Row(long seq, long ms) =>
            new(
                0,
                Lane.Reading,
                "P1-TST-W001",
                seq,
                ms,
                JsonSerializer.SerializeToUtf8Bytes(Reading("P1-TST-W001", seq, ms), Json.Options)
            );
        var rows = new[] { Row(1, ms1), Row(2, ms2) };
        await bBuffer.WriteTickAsync(
            new TickBatch(rows, [new MachineState("P1-TST-W001", 2, 0, ms2)]),
            ct
        );
        using (
            var producer = new ProducerBuilder<string, byte[]>(
                KafkaConfigs.Producer(bootstrap)
            ).Build()
        )
        {
            foreach (var row in rows)
                await producer.ProduceAsync(
                    TopicNames.Readings,
                    new Message<string, byte[]>
                    {
                        Key = row.MachineId,
                        Value = row.Payload,
                        Timestamp = new Timestamp(row.DeviceTimeMs, TimestampType.CreateTime),
                    },
                    ct
                );
        }
        bBuffer.Stats().Readings.Should().Be(2);

        await using var aBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-a.db"));
        var a = Track(NewLease("tst-a", aBuffer, new LeaseHarness()));
        await a.StartAsync(CancellationToken.None);
        (await EventuallyAsync(() => a.IsActive, TimeSpan.FromSeconds(15))).Should().BeTrue();

        var b = Track(NewLease("tst-b", bBuffer, new LeaseHarness()));
        await b.StartAsync(CancellationToken.None);
        var emptied = await EventuallyAsync(
            () => bBuffer.Stats().Readings == 0,
            TimeSpan.FromSeconds(12)
        );
        emptied.Should().BeTrue("the standby reconciles its buffer with the plant log");
        bBuffer.Stats().Readings.Should().Be(0);
    }

    [Fact]
    public async Task A_lost_session_flushes_the_sender_and_the_next_assignment_starts_a_new_term()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var aBuffer = GatewayBuffer.Open(Path.Combine(_dir, "tst-a.db"));
        var harness = new LeaseHarness();
        var a = Track(NewLease("tst-a", aBuffer, harness));
        await a.StartAsync(CancellationToken.None);
        (await EventuallyAsync(() => a.IsActive, TimeSpan.FromSeconds(15))).Should().BeTrue();
        harness.Starts.Should().Be(1);

        await _kafka.PauseAsync(ct);
        // The session expires while the broker is unreachable: the sender token is cancelled at once, the term token is not.
        var senderCancelled = await EventuallyAsync(
            () => harness.SenderCancelled.Task.IsCompleted,
            TimeSpan.FromSeconds(12)
        );
        senderCancelled.Should().BeTrue("the lost session cancels only the sender token");
        harness.TermCancelled.Task.IsCompleted.Should().BeFalse();
        a.IsActive.Should().BeTrue();

        await _kafka.UnpauseAsync(ct);
        // The repaired member rejoins; the assignment after the lost session starts a new term.
        var restarted = await EventuallyAsync(
            () => a.IsActive && harness.Starts == 2,
            TimeSpan.FromSeconds(30)
        );
        restarted.Should().BeTrue();
        a.IsActive.Should().BeTrue();
        harness.Starts.Should().Be(2);
    }

    private GatewayLease Track(GatewayLease lease)
    {
        _leases.Add(lease);
        return lease;
    }

    private GatewayLease NewLease(string instance, GatewayBuffer buffer, LeaseHarness harness) =>
        harness.Lease = new(
            _kafka.GetBootstrapAddress(),
            "TST",
            instance,
            buffer,
            Machines,
            harness.WhileActive,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );

    private static MachineReading Reading(string machineId, long seq, long deviceTimeMs) =>
        new(
            machineId,
            "WeldRobot",
            "TST",
            seq,
            DateTimeOffset.FromUnixTimeMilliseconds(deviceTimeMs),
            new Dictionary<string, double> { ["weld_current_ka"] = 11.8 }
        );

    private static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(100);
        }
        return condition();
    }

    private sealed class LeaseHarness
    {
        private int _starts;

        public int Starts => Volatile.Read(ref _starts);
        public GatewayLease? Lease { get; set; }
        public TaskCompletionSource SenderCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TermCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WhileActive(CancellationToken termToken, CancellationToken senderToken)
        {
            Interlocked.Increment(ref _starts);
            using var senderStop = senderToken.Register(() => SenderCancelled.TrySetResult());
            using var termStop = termToken.Register(() => TermCancelled.TrySetResult());
            Lease!.MarkActive(); // as the real term does once its numbering is resumed
            try
            {
                await Task.Delay(Timeout.Infinite, termToken);
            }
            catch (OperationCanceledException) { }
        }
    }
}
