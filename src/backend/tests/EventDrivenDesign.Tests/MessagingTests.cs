using System.Text;
using AwesomeAssertions;
using Confluent.Kafka;
using EventDrivenDesign.Messaging;
using Xunit;

namespace EventDrivenDesign.Tests;

public class MessagingTests
{
    private static string Header(Headers h, string key) =>
        Encoding.UTF8.GetString(h.GetLastBytes(key));

    [Fact]
    public void Producer_settings_match_the_spec()
    {
        var c = KafkaConfigs.Producer("kafka:9092");
        c.BootstrapServers.Should().Be("kafka:9092");
        c.Acks.Should().Be(Acks.All);
        c.EnableIdempotence.Should().BeTrue();
        c.LingerMs.Should().Be(20);
        c.CompressionType.Should().Be(CompressionType.Lz4);
        c.MessageTimeoutMs.Should().Be(30000);
        c.QueueBufferingMaxKbytes.Should().Be(16384);
        c.QueueBufferingMaxMessages.Should().Be(20000);
    }

    [Fact]
    public void Lease_settings_match_the_spec()
    {
        var c = KafkaConfigs.Lease("k:9092", "BDY", "gateway-bdy-a");
        c.BootstrapServers.Should().Be("k:9092");
        c.GroupId.Should().Be("gateway-lease-bdy");
        c.GroupInstanceId.Should().Be("gateway-bdy-a");
        c.SessionTimeoutMs.Should().Be(6000);
        c.HeartbeatIntervalMs.Should().Be(2000);
        c.ReconnectBackoffMaxMs.Should().Be(1000); // back inside the 6 s session after a plant outage
        c.PartitionAssignmentStrategy.Should().Be(PartitionAssignmentStrategy.CooperativeSticky);
        c.EnableAutoCommit.Should().BeFalse();
        c.AutoOffsetReset.Should().Be(AutoOffsetReset.Latest);
    }

    [Fact]
    public void Store_writer_settings_match_the_spec()
    {
        var c = KafkaConfigs.StoreWriter("kafka:9092", "store-writers");
        c.BootstrapServers.Should().Be("kafka:9092");
        c.GroupId.Should().Be("store-writers");
        c.EnableAutoCommit.Should().BeTrue();
        c.EnableAutoOffsetStore.Should().BeFalse();
        c.AutoOffsetReset.Should().Be(AutoOffsetReset.Earliest);
        c.PartitionAssignmentStrategy.Should().Be(PartitionAssignmentStrategy.CooperativeSticky);
        c.QueuedMaxMessagesKbytes.Should().Be(16384);
        c.TopicMetadataRefreshIntervalMs.Should().Be(5000);
    }

    [Fact]
    public void Tail_settings_use_a_unique_group_and_never_commit()
    {
        var a = KafkaConfigs.Tail("kafka:9092");
        var b = KafkaConfigs.Tail("kafka:9092");
        a.GroupId.Should().StartWith("api-tail-").And.NotBe(b.GroupId);
        a.EnableAutoCommit.Should().BeFalse();
        a.QueuedMaxMessagesKbytes.Should().Be(16384);
    }

    [Fact]
    public void Admin_settings_carry_the_bootstrap_servers() =>
        KafkaConfigs.Admin("kafka:9092").BootstrapServers.Should().Be("kafka:9092");

    [Fact]
    public void Reading_headers()
    {
        var ms = DateTimeOffset.Parse("2026-11-12T10:15:03.120Z").ToUnixTimeMilliseconds();
        var h = CloudEvents.For(false, "BDY", "P1-BDY-W042", 184223, ms);
        Header(h, "ce_specversion").Should().Be("1.0");
        Header(h, "ce_id").Should().Be("P1-BDY-W042-184223");
        Header(h, "ce_type").Should().Be("com.eventdrivendesign.machine.reading.v1");
        Header(h, "ce_source").Should().Be("/plant1/BDY/gateway");
        Header(h, "ce_time").Should().Be("2026-11-12T10:15:03.120Z");
    }

    [Fact]
    public void Alarm_headers()
    {
        var ms = DateTimeOffset.Parse("2026-11-12T10:15:03.120Z").ToUnixTimeMilliseconds();
        var h = CloudEvents.For(true, "BDY", "P1-BDY-W042", 7, ms);
        Header(h, "ce_id").Should().Be("P1-BDY-W042-a7");
        Header(h, "ce_type").Should().Be("com.eventdrivendesign.machine.alarm.v1");
    }

    [Fact]
    public void Backoff_is_bounded_with_full_jitter()
    {
        var rng = new Random(1);
        for (var attempt = 0; attempt <= 30; attempt++)
        {
            var cap = TimeSpan.FromSeconds(Math.Min(30, 0.1 * Math.Pow(2, attempt)));
            var d = Retry.Backoff(attempt, rng);
            d.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero).And.BeLessThanOrEqualTo(cap);
            if (attempt == 0)
                d.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        }
    }
}
