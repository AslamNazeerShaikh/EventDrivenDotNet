using System.Text;
using AwesomeAssertions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;
using Testcontainers.Kafka;
using Xunit;

namespace EventDrivenDesign.IntegrationTests;

public sealed class SenderTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();
    private readonly string _dir = Directory.CreateTempSubdirectory("sender-tests-").FullName;

    public async ValueTask InitializeAsync()
    {
        await _kafka.StartAsync(TestContext.Current.CancellationToken);
        using var admin = new AdminClientBuilder(KafkaConfigs.Admin(_kafka.GetBootstrapAddress())).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = TopicNames.Readings, NumPartitions = 12, ReplicationFactor = 1 },
            new TopicSpecification { Name = TopicNames.Alarms, NumPartitions = 3, ReplicationFactor = 1 },
        ]);
    }

    public async ValueTask DisposeAsync()
    {
        await _kafka.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Rows_stay_while_the_broker_is_paused_then_drain_exactly()
    {
        var bootstrap = _kafka.GetBootstrapAddress();
        await using var buffer = GatewayBuffer.Open(Path.Combine(_dir, "buffer.db"));
        var collector = new Collector("P1", "BDY", 7, buffer.LoadState());
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            if (i == 2) collector.InjectFault("P1-BDY-W042", AlarmKind.Overheat).Should().BeTrue();
            await buffer.WriteTickAsync(collector.Tick(now.AddSeconds(i)), TestContext.Current.CancellationToken);
        }
        buffer.Stats().Should().Match<BufferStats>(s => s.Readings == 300 && s.Alarms == 1);

        using var producer = new ProducerBuilder<string, byte[]>(KafkaConfigs.Producer(bootstrap)).Build();
        var sender = new Sender(buffer, producer, "BDY", TimeProvider.System);
        using var cts = new CancellationTokenSource();
        var run = sender.RunAsync(cts.Token);

        await _kafka.PauseAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var buffered = buffer.Stats();
        (buffered.Readings + buffered.Alarms).Should().Be(301, "nothing is acknowledged while the broker is paused");
        sender.Sent.Should().Be(0);

        await _kafka.UnpauseAsync(TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && buffer.Stats() is { Readings: > 0 } or { Alarms: > 0 })
            await Task.Delay(200, TestContext.Current.CancellationToken);
        buffer.Stats().Should().Match<BufferStats>(s => s.Readings == 0 && s.Alarms == 0);

        await cts.CancelAsync();
        await run;

        var (readings, alarms) = ConsumeBoth(bootstrap);
        readings.Should().HaveCount(300);
        readings.Select(r => r.CeId).Distinct().Should().HaveCount(300);
        alarms.Should().ContainSingle().Which.CeId.Should().Be("P1-BDY-W042-a1");
        sender.Sent.Should().Be(301);
    }

    private static (List<Received> Readings, List<Received> Alarms) ConsumeBoth(string bootstrap)
    {
        var config = KafkaConfigs.Tail(bootstrap);
        config.AutoOffsetReset = AutoOffsetReset.Earliest;
        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe([TopicNames.Readings, TopicNames.Alarms]);
        List<Received> readings = [], alarms = [];
        // Stop once the topics have been quiet for 5 s, so a duplicate would still be seen.
        var quietSince = DateTime.UtcNow;
        var hardStop = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < hardStop && DateTime.UtcNow - quietSince < TimeSpan.FromSeconds(5))
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null) continue;
            quietSince = DateTime.UtcNow;
            var received = new Received(result.Message.Key,
                Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes("ce_id")), result.Message.Timestamp.UnixTimestampMs);
            (result.Topic == TopicNames.Readings ? readings : alarms).Add(received);
        }
        return (readings, alarms);
    }

    private sealed record Received(string Key, string CeId, long TimestampMs);
}
