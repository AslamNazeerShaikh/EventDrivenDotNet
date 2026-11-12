using System.Globalization;
using System.Text;
using Confluent.Kafka;

namespace EventDrivenDesign.Messaging;

/// <summary>CloudEvents binary-mode Kafka headers (doc 00 §4).</summary>
public static class CloudEvents
{
    public static Headers For(bool isAlarm, string shop, string machineId, long seq, long deviceTimeMs)
    {
        var time = DateTimeOffset.FromUnixTimeMilliseconds(deviceTimeMs).UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return new Headers
        {
            { "ce_specversion", Utf8("1.0") },
            { "ce_id", Utf8(isAlarm ? $"{machineId}-a{seq}" : $"{machineId}-{seq}") },
            { "ce_type", Utf8(isAlarm ? "com.eventdrivendesign.machine.alarm.v1" : "com.eventdrivendesign.machine.reading.v1") },
            { "ce_source", Utf8($"/plant1/{shop}/gateway") },
            { "ce_time", Utf8(time) },
        };
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
