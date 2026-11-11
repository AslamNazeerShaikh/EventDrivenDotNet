using System.Text;
using Confluent.Kafka;

namespace EventDrivenDesign.Messaging;

/// <summary>
/// CloudEvents-style Kafka record headers from spec section 5
/// (<c>ce_*</c> plus run/stream/sequence tracking).
/// </summary>
public static class Envelope
{
    /// <summary>Header carrying the event id (UUIDv7).</summary>
    public const string Id = "ce_id";

    /// <summary>Header carrying the event type (<c>OrderPlaced</c> or <c>PaymentCaptured</c>).</summary>
    public const string Type = "ce_type";

    /// <summary>Header carrying the producing service id.</summary>
    public const string Source = "ce_source";

    /// <summary>Header carrying the production timestamp.</summary>
    public const string Time = "ce_time";

    /// <summary>Header carrying the command id of the Start that began the run.</summary>
    public const string RunId = "x-run-id";

    /// <summary>Header carrying the producer stream id.</summary>
    public const string StreamId = "x-stream-id";

    /// <summary>Header carrying the sequence number within the stream.</summary>
    public const string Seq = "x-seq";

    /// <summary>
    /// Writes the envelope headers for one event onto the record headers.
    /// </summary>
    /// <param name="headers">The record headers to write to.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="source">The producing service id.</param>
    /// <param name="at">The production timestamp.</param>
    /// <param name="runId">The run id.</param>
    /// <param name="streamId">The stream id.</param>
    /// <param name="seq">The sequence number within the stream.</param>
    public static void Write(
        Headers headers,
        string eventId,
        string eventType,
        string source,
        DateTimeOffset at,
        string runId,
        string streamId,
        long seq)
    {
        headers.Add(Id, Encoding.UTF8.GetBytes(eventId));
        headers.Add(Type, Encoding.UTF8.GetBytes(eventType));
        headers.Add(Source, Encoding.UTF8.GetBytes(source));
        headers.Add(Time, Encoding.UTF8.GetBytes(at.ToUnixTimeMilliseconds().ToString()));
        headers.Add(RunId, Encoding.UTF8.GetBytes(runId));
        headers.Add(StreamId, Encoding.UTF8.GetBytes(streamId));
        headers.Add(Seq, Encoding.UTF8.GetBytes(seq.ToString()));
    }

    /// <summary>
    /// Tries to read one header value as text.
    /// </summary>
    /// <param name="headers">The record headers to read from.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The decoded value, or null when missing.</param>
    /// <returns>True when the header exists.</returns>
    public static bool TryRead(Headers headers, string name, out string? value)
    {
        if (headers.TryGetLastBytes(name, out byte[]? bytes) && bytes is not null)
        {
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        value = null;
        return false;
    }
}
