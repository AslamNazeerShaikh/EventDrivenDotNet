using System.Text.RegularExpressions;
using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using Xunit;

namespace EventDrivenDesign.Tests;

/// <summary>librdkafka compiles subscription patterns as POSIX ERE, which has no \d, \w or \s. .NET accepts them, so the guard checks the text.</summary>
public sealed class TopicNamesTests
{
    [Fact]
    public void Lease_topic_is_per_shop() =>
        TopicNames.Lease("BDY").Should().Be("gateway.lease.bdy");

    [Theory]
    [InlineData(TopicNames.CentralReadingsPattern)]
    [InlineData(TopicNames.CentralAlarmsPattern)]
    public void Pattern_uses_no_class_shorthand_that_posix_ere_lacks(string pattern) =>
        pattern.Should().NotContainAny(@"\d", @"\w", @"\s");

    [Theory]
    [InlineData(TopicNames.CentralReadingsPattern, "plant1.machine.readings", true)]
    [InlineData(TopicNames.CentralReadingsPattern, "plant12.machine.readings", true)]
    [InlineData(TopicNames.CentralReadingsPattern, "plant1.machine.readings.dlq", false)]
    [InlineData(TopicNames.CentralReadingsPattern, "xplant1.machine.readings", false)]
    [InlineData(TopicNames.CentralReadingsPattern, "plant.machine.readings", false)]
    [InlineData(TopicNames.CentralReadingsPattern, "plant1.heartbeats", false)]
    [InlineData(TopicNames.CentralReadingsPattern, "plant1.machine.alarms", false)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant1.machine.alarms", true)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant12.machine.alarms", true)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant1.machine.alarms.dlq", false)]
    [InlineData(TopicNames.CentralAlarmsPattern, "xplant1.machine.alarms", false)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant.machine.alarms", false)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant1.heartbeats", false)]
    [InlineData(TopicNames.CentralAlarmsPattern, "plant1.machine.readings", false)]
    public void Pattern_matches_only_the_mirrored_topic(
        string pattern,
        string topic,
        bool expected
    ) => Regex.IsMatch(topic, pattern, RegexOptions.ECMAScript).Should().Be(expected);
}
