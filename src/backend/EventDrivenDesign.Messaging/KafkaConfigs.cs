using Confluent.Kafka;

namespace EventDrivenDesign.Messaging;

/// <summary>librdkafka settings from doc 00 §5 and §7. Queue caps keep containers from being OOM-killed during an outage (§3).</summary>
public static class KafkaConfigs
{
    public static ProducerConfig Producer(string bootstrap) =>
        new()
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

    /// <summary>
    /// The classic group protocol (librdkafka's default): the newer consumer protocol sets session timeouts on the
    /// broker with a 45 s minimum, while 6,000 ms is the broker's default group.min.session.timeout.ms.
    /// Cooperative-sticky moves only the partitions that must move, so a joiner never evicts the current owner.
    /// </summary>
    public static ConsumerConfig Lease(string bootstrap, string shop, string instanceId) =>
        new()
        {
            BootstrapServers = bootstrap,
            GroupId = $"gateway-lease-{shop.ToLowerInvariant()}",
            GroupInstanceId = instanceId,
            SessionTimeoutMs = 6000,
            HeartbeatIntervalMs = 2000,
            // The broker restarts each static member's 6 s session when it reloads the group after an outage;
            // librdkafka's default 10 s reconnect backoff would let the session expire and move the lease.
            ReconnectBackoffMaxMs = 1000,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest,
        };

    public static ConsumerConfig StoreWriter(string bootstrap, string groupId) =>
        new()
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

    public static ConsumerConfig Tail(string bootstrap) =>
        new()
        {
            BootstrapServers = bootstrap,
            GroupId = "api-tail-" + Guid.NewGuid(),
            EnableAutoCommit = false,
            QueuedMaxMessagesKbytes = 16384,
        };

    public static AdminClientConfig Admin(string bootstrap) =>
        new() { BootstrapServers = bootstrap };
}
