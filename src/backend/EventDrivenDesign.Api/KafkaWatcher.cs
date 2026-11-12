using Confluent.Kafka;
using Confluent.Kafka.Admin;
using EventDrivenDesign.Messaging;

namespace EventDrivenDesign.Api;

/// <summary>
/// Watches one topic on one cluster: end offsets (and a group's committed offsets) on demand, and a tail consumer
/// that reports the timestamp of every record it sees. The tail never commits and never joins a group's work.
/// </summary>
public sealed class KafkaWatcher(string bootstrap, string topic, string? group, Action<DateTimeOffset> seen, ILogger log) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private IAdminClient? _admin;

    /// <summary>
    /// Throws when the broker cannot be reached: the caller leaves the last-success time alone.
    /// A failed poll drops the admin client: after a broker restart a long-lived one kept timing out, a new one connects at once.
    /// </summary>
    public async Task<OffsetSample> PollOffsetsAsync()
    {
        var admin = _admin ??= new AdminClientBuilder(KafkaConfigs.Admin(bootstrap)).Build();
        try
        {
            return await PollAsync(admin);
        }
        catch
        {
            _admin = null;
            admin.Dispose();
            throw;
        }
    }

    private async Task<OffsetSample> PollAsync(IAdminClient admin)
    {
        var partitions = Partitions(admin, topic);
        if (partitions.Count == 0) return new OffsetSample(new Dictionary<int, long>(), new Dictionary<int, long>());

        var latest = await admin.ListOffsetsAsync(
            partitions.Select(p => new TopicPartitionOffsetSpec { TopicPartition = new TopicPartition(topic, p), OffsetSpec = OffsetSpec.Latest() }),
            new ListOffsetsOptions { RequestTimeout = Timeout });
        var end = latest.ResultInfos.ToDictionary(r => r.TopicPartitionOffsetError.Partition.Value, r => r.TopicPartitionOffsetError.Offset.Value);

        var committed = new Dictionary<int, long>();
        if (group is not null)
        {
            var groups = await admin.ListConsumerGroupOffsetsAsync(
                [new ConsumerGroupTopicPartitions(group, [.. partitions.Select(p => new TopicPartition(topic, p))])],
                new ListConsumerGroupOffsetsOptions { RequestTimeout = Timeout });
            foreach (var tpo in groups.SelectMany(g => g.Partitions))
                if (tpo.Offset.Value >= 0) committed[tpo.Partition.Value] = tpo.Offset.Value; // Unset: nothing committed yet
        }
        return new OffsetSample(end, committed);
    }

    /// <summary>Long-running: reconnects after any failure. Blocks its thread, so start it with LongRunning.</summary>
    public void RunTail(CancellationToken ct)
    {
        var firstLook = true; // the first look assigns at the end; a topic that appears later is read from its start
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var consumer = new ConsumerBuilder<Ignore, Ignore>(KafkaConfigs.Tail(bootstrap)).Build();
                using var admin = new AdminClientBuilder(KafkaConfigs.Admin(bootstrap)).Build(); // the tail's own, for partition discovery
                HashSet<int> assigned = [];
                var nextLook = DateTime.MinValue;
                while (!ct.IsCancellationRequested)
                {
                    if (DateTime.UtcNow >= nextLook)
                    {
                        nextLook = DateTime.UtcNow.AddSeconds(5);
                        try
                        {
                            var added = Partitions(admin, topic).Where(p => !assigned.Contains(p)).ToList();
                            if (added.Count > 0)
                            {
                                var position = firstLook ? Offset.End : Offset.Beginning;
                                consumer.IncrementalAssign(added.Select(p => new TopicPartitionOffset(topic, p, position)));
                                assigned.UnionWith(added);
                            }
                            firstLook = false;
                        }
                        catch (KafkaException e)
                        {
                            log.LogWarning("Tail of {Topic} cannot reach {Bootstrap}: {Reason}", topic, bootstrap, e.Error.Reason);
                        }
                    }

                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result?.Message is { Timestamp.Type: not TimestampType.NotAvailable } m) seen(m.Timestamp.UtcDateTime);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Tail of {Topic} failed; restarting", topic);
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
            }
        }
    }

    public void Dispose() => _admin?.Dispose();

    private static List<int> Partitions(IAdminClient admin, string topic)
    {
        var meta = admin.GetMetadata(topic, Timeout).Topics.Single();
        return meta.Error.Code == ErrorCode.UnknownTopicOrPart ? [] : meta.Error.IsError
            ? throw new KafkaException(meta.Error) : [.. meta.Partitions.Select(p => p.PartitionId)];
    }
}
