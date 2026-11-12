using Confluent.Kafka;

namespace EventDrivenDesign.Messaging;

/// <summary>librdkafka settings from doc 00 §5 and §7. Queue caps keep containers from being OOM-killed during an outage (§3).</summary>
public static class KafkaConfigs
{
    public static ProducerConfig Producer(string bootstrap) => new()
    {
        BootstrapServers = bootstrap,
        Acks = Acks.All,
        EnableIdempotence = true,
        LingerMs = 20,
        CompressionType = CompressionType.Lz4,
        MessageTimeoutMs = 30000,
        QueueBufferingMaxKbytes = 16384,
        QueueBufferingMaxMessages = 20000,
    };

    public static ConsumerConfig StoreWriter(string bootstrap, string groupId) => new()
    {
        BootstrapServers = bootstrap,
        GroupId = groupId,
        EnableAutoCommit = true,
        EnableAutoOffsetStore = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        QueuedMaxMessagesKbytes = 16384,
        TopicMetadataRefreshIntervalMs = 5000,
    };

    public static ConsumerConfig Tail(string bootstrap) => new()
    {
        BootstrapServers = bootstrap,
        GroupId = "api-tail-" + Guid.NewGuid(),
        EnableAutoCommit = false,
        QueuedMaxMessagesKbytes = 16384,
    };

    public static AdminClientConfig Admin(string bootstrap) => new() { BootstrapServers = bootstrap };
}
