using System.Collections.Concurrent;
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

public sealed class PlantLogReaderTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();

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
        ]);
    }

    public async ValueTask DisposeAsync()
    {
        await _kafka.DisposeAsync();
    }

    [Fact]
    public async Task Newest_position_comes_from_the_log()
    {
        var bootstrap = _kafka.GetBootstrapAddress();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);
        using var producer = new ProducerBuilder<string, byte[]>(
            KafkaConfigs.Producer(bootstrap)
        ).Build();
        for (var seq = 1; seq <= 50; seq++)
            await producer.ProduceAsync(
                TopicNames.Readings,
                new Message<string, byte[]>
                {
                    Key = "P1-BDY-W042",
                    Value = JsonSerializer.SerializeToUtf8Bytes(
                        Reading("P1-BDY-W042", seq, t0.AddSeconds(seq)),
                        Json.Options
                    ),
                },
                ct
            );
        for (var alarmSeq = 1; alarmSeq <= 3; alarmSeq++)
            await producer.ProduceAsync(
                TopicNames.Alarms,
                new Message<string, byte[]>
                {
                    Key = "P1-BDY-W042",
                    Value = JsonSerializer.SerializeToUtf8Bytes(
                        new MachineAlarm(
                            "P1-BDY-W042",
                            alarmSeq,
                            alarmSeq * 10,
                            t0.AddSeconds(alarmSeq),
                            AlarmKind.Overheat,
                            "warning",
                            "current",
                            12.5,
                            11
                        ),
                        Json.Options
                    ),
                },
                ct
            );

        var newest = await PlantLogReader.NewestAsync(
            bootstrap,
            ["P1-BDY-W042", "P1-BDY-W001"],
            ct
        );

        newest.Should().NotContainKey("P1-BDY-W001");
        newest["P1-BDY-W042"]
            .Should()
            .Be(new LogPosition(50, 3, t0.AddSeconds(50).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task Doubling_scan_reaches_past_other_traffic()
    {
        var bootstrap = _kafka.GetBootstrapAddress();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);
        using var producer = new ProducerBuilder<string, byte[]>(
            KafkaConfigs.Producer(bootstrap)
        ).Build();
        var failures = new ConcurrentBag<string>();
        void Produce(string machine, long seq) =>
            producer.Produce(
                TopicNames.Readings,
                new Message<string, byte[]>
                {
                    Key = machine,
                    Value = JsonSerializer.SerializeToUtf8Bytes(
                        Reading(machine, seq, t0.AddSeconds(seq % 3_000)),
                        Json.Options
                    ),
                },
                report =>
                {
                    if (report.Error.IsError)
                        failures.Add(report.Error.Reason);
                }
            );
        // W042's readings first: they sit at the start of its partition.
        for (var seq = 1; seq <= 50; seq++)
            Produce("P1-BDY-W042", seq);
        var others = Enumerable
            .Range(1, 41)
            .Where(n => n != 42)
            .Select(n => $"P1-BDY-W{n:000}")
            .ToArray();
        for (var i = 0; i < 30_000; i++)
        {
            Produce(others[i % others.Length], 10_000 + i);
            if (i % 1_000 == 999)
                producer.Flush(TimeSpan.FromSeconds(30)); // the queue holds 20,000 messages at most
        }
        producer.Flush(TimeSpan.FromSeconds(30));
        failures.Should().BeEmpty("every reading is delivered before the reconcile runs");

        var newest = await PlantLogReader.NewestAsync(bootstrap, ["P1-BDY-W042"], ct);

        newest["P1-BDY-W042"].LastSeq.Should().Be(50);
    }

    private static MachineReading Reading(string machine, long seq, DateTimeOffset time) =>
        new(
            machine,
            "WeldRobot",
            "BDY",
            seq,
            time,
            new Dictionary<string, double> { ["current"] = 11.5 }
        );
}
