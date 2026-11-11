namespace EventDrivenDesign.Contracts;

/// <summary>
/// Kafka topic names from spec section 5.
/// </summary>
public static class TopicNames
{
    /// <summary>Data topic: 6 partitions, keyed by entity id.</summary>
    public const string Events = "demo.events";

    /// <summary>Dead-letter topic for unparseable records.</summary>
    public const string EventsDlq = "demo.events.dlq";

    /// <summary>Compacted command topic, keyed by target service id.</summary>
    public const string ControlCommands = "control.commands";

    /// <summary>Command acknowledgement topic, keyed by command id.</summary>
    public const string ControlAcks = "control.acks";

    /// <summary>Heartbeat topic, keyed by service id.</summary>
    public const string TelemetryHeartbeats = "telemetry.heartbeats";
}
