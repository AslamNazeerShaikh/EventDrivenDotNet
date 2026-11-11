namespace EventDrivenDesign.Messaging;

/// <summary>
/// Kafka connection options, bound via the Options pattern.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Gets or sets the broker bootstrap servers.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";
}

/// <summary>
/// Heartbeat publishing options, bound via the Options pattern.
/// </summary>
public sealed class HeartbeatOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Heartbeat";

    /// <summary>Gets or sets the id this service reports under.</summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the seconds between heartbeats.</summary>
    public int IntervalSeconds { get; set; } = 1;
}
