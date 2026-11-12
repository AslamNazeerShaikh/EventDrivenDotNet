using AwesomeAssertions;
using Confluent.Kafka;
using EventDrivenDesign.Consumer;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EventDrivenDesign.Tests;

public sealed class RateCapTests
{
    [Fact]
    public void Batch_faster_than_the_cap_waits_out_the_rest_of_its_budget() =>
        new RateCap(1000).DelayAfter(5000, TimeSpan.FromSeconds(1)).Should().Be(TimeSpan.FromSeconds(4));

    [Fact]
    public void Batch_within_the_cap_does_not_wait() =>
        new RateCap(1000).DelayAfter(500, TimeSpan.FromSeconds(1)).Should().Be(TimeSpan.Zero);

    [Fact]
    public void Zero_rate_means_uncapped() =>
        new RateCap(0).DelayAfter(1_000_000, TimeSpan.Zero).Should().Be(TimeSpan.Zero);
}

public sealed class RateMeterTests
{
    [Fact]
    public void Rate_is_the_count_over_a_ten_second_window_and_old_entries_age_out()
    {
        var time = new FakeTimeProvider();
        var meter = new RateMeter(time);
        meter.Add(500);
        time.Advance(TimeSpan.FromSeconds(4));
        meter.Add(500);
        meter.PerSecond.Should().Be(100);
        time.Advance(TimeSpan.FromSeconds(7)); // the first 500 is now 11 s old
        meter.PerSecond.Should().Be(50);
    }
}

public sealed class LastPerPartitionTests
{
    private static ConsumeResult<string, byte[]> Rec(string topic, int partition, long offset) =>
        new() { Topic = topic, Partition = partition, Offset = offset };

    [Fact]
    public void Picks_the_last_consumed_record_of_each_partition()
    {
        var batch = new[] { Rec("t", 0, 5), Rec("t", 1, 9), Rec("t", 0, 6), Rec("u", 0, 1), Rec("t", 1, 10) };

        var last = BatchLoop<object>.LastPerPartition(batch).Select(r => (r.Topic, r.Partition.Value, r.Offset.Value)).ToList();

        last.Should().BeEquivalentTo([("t", 0, 6L), ("t", 1, 10L), ("u", 0, 1L)]);
    }
}
