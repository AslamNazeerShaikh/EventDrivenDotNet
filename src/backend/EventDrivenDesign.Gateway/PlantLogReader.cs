using System.Text.Json;
using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Gateway;

/// <summary>
/// Rebuilds a buffer's numbering from the plant topics (doc 02 §4.3). The consumer assigns explicit
/// offsets and never joins a group or commits. Each topic is scanned backwards in lock-step rounds;
/// round r reads the next 1,000 × 2^r records below where the previous round started, on every
/// partition not yet at its low watermark.
/// </summary>
public static class PlantLogReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The newest reading and alarm per machine in the plant topics. Machines with no reading in the
    /// topics are absent. A record that does not parse is skipped.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, LogPosition>> NewestAsync(
        string bootstrap,
        IReadOnlyCollection<string> machines,
        CancellationToken ct
    )
    {
        var wanted = new HashSet<string>(machines);
        var readings = new Dictionary<string, (long Seq, long TimeMs)>();
        var alarms = new Dictionary<string, long>();
        using var admin = new AdminClientBuilder(KafkaConfigs.Admin(bootstrap)).Build();
        using var consumer = new ConsumerBuilder<Ignore, byte[]>(
            KafkaConfigs.Tail(bootstrap)
        ).Build();

        await ScanTopicAsync(
            consumer,
            admin,
            TopicNames.Readings,
            wanted,
            payload =>
            {
                MachineReading? reading;
                try
                {
                    reading = JsonSerializer.Deserialize<MachineReading>(payload, Json.Options);
                }
                catch (JsonException)
                {
                    return null;
                }

                if (reading is null || !wanted.Contains(reading.MachineId))
                    return null;
                var id = reading.MachineId;
                if (!readings.TryGetValue(id, out var best) || reading.Seq > best.Seq)
                    readings[id] = (reading.Seq, reading.DeviceTime.ToUnixTimeMilliseconds());
                return id;
            },
            ct
        );

        await ScanTopicAsync(
            consumer,
            admin,
            TopicNames.Alarms,
            wanted,
            payload =>
            {
                MachineAlarm? alarm;
                try
                {
                    alarm = JsonSerializer.Deserialize<MachineAlarm>(payload, Json.Options);
                }
                catch (JsonException)
                {
                    return null;
                }

                if (alarm is null || !wanted.Contains(alarm.MachineId))
                    return null;
                var id = alarm.MachineId;
                if (!alarms.TryGetValue(id, out var best) || alarm.AlarmSeq > best)
                    alarms[id] = alarm.AlarmSeq;
                return id;
            },
            ct
        );

        var result = new Dictionary<string, LogPosition>(readings.Count);
        foreach (var (machine, reading) in readings)
            result[machine] = new LogPosition(
                reading.Seq,
                alarms.GetValueOrDefault(machine),
                reading.TimeMs
            );
        return result;
    }

    /// <summary>NewestAsync until the broker answers (Retry.Backoff between attempts), then the reconcile.</summary>
    public static async Task ReconcileBufferAsync(
        GatewayBuffer buffer,
        string bootstrap,
        IReadOnlyCollection<string> machines,
        ILogger log,
        CancellationToken ct
    )
    {
        IReadOnlyDictionary<string, LogPosition> newest;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                newest = await NewestAsync(bootstrap, machines, ct);
                break;
            }
            catch (KafkaException e)
            {
                var delay = Retry.Backoff(attempt, Random.Shared);
                log.LogWarning(
                    e,
                    "The plant log does not answer (attempt {Attempt}); retrying in {Delay}",
                    attempt + 1,
                    delay
                );
                await Task.Delay(delay, ct);
            }
        }

        await buffer.ReconcileAsync(newest, ct);
        log.LogInformation(
            "Reconciled the buffer with the plant log for {Count} machines",
            newest.Count
        );
    }

    private static async Task ScanTopicAsync(
        IConsumer<Ignore, byte[]> consumer,
        IAdminClient admin,
        string topic,
        HashSet<string> machines,
        Func<byte[], string?> onRecord,
        CancellationToken ct
    )
    {
        if (machines.Count == 0)
            return; // nobody to look for
        var meta = admin.GetMetadata(topic, Timeout).Topics.Single();
        if (meta.Error.Code == ErrorCode.UnknownTopicOrPart)
            return; // the topic does not exist: there is nothing to read
        if (meta.Error.IsError)
            throw new KafkaException(meta.Error);

        var low = new Dictionary<int, long>();
        var pos = new Dictionary<int, long>(); // window end for the next round; starts at the high watermark
        foreach (var p in meta.Partitions.Select(p => p.PartitionId))
        {
            var watermarks = consumer.QueryWatermarkOffsets(new TopicPartition(topic, p), Timeout);
            low[p] = watermarks.Low.Value;
            pos[p] = watermarks.High.Value;
        }

        var seen = new HashSet<string>();
        var round = 0;
        while (seen.Count < machines.Count && pos.Any(kv => kv.Value > low[kv.Key]))
        {
            var window = 1000L << Math.Min(round, 53); // 1,000 × 2^r; the shift is capped to stay positive
            round++;
            var start = new Dictionary<int, long>();
            var target = new Dictionary<int, long>();
            var assigns = new List<TopicPartitionOffset>();
            foreach (var p in pos.Keys)
            {
                if (pos[p] <= low[p])
                    continue; // already at its low watermark
                start[p] = Math.Max(low[p], pos[p] - window);
                target[p] = pos[p];
                assigns.Add(new TopicPartitionOffset(topic, p, start[p]));
            }
            consumer.Assign(assigns);

            var pending = new HashSet<int>(target.Keys);
            var idle = 0;
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result is null)
                {
                    // A partition whose window has only compacted gaps may never reach its target
                    // by delivery; the local fetch position says when it has passed it.
                    foreach (var p in pending.ToList())
                        if (FetchPosition(consumer, topic, p) >= target[p])
                            pending.Remove(p);
                    if (++idle >= 4) // 2 s quiet across the window: stop waiting on it
                        pending.Clear();
                    continue;
                }

                idle = 0;
                var part = result.Partition.Value;
                var offset = result.Offset.Value;
                if (offset < target[part])
                {
                    var machine = onRecord(result.Message.Value);
                    if (machine is not null)
                        seen.Add(machine);
                } // otherwise the fetch walked past the window (compacted gaps); the round owns nothing there

                if (offset + 1 >= target[part])
                    pending.Remove(part);
            }

            foreach (var p in target.Keys)
                pos[p] = start[p];
        }
    }

    private static long FetchPosition(
        IConsumer<Ignore, byte[]> consumer,
        string topic,
        int partition
    )
    {
        try
        {
            return consumer.Position(new TopicPartition(topic, partition)).Value;
        }
        catch (KafkaException)
        {
            return 0; // unknown is below every target; the idle break bounds the round instead
        }
    }
}
