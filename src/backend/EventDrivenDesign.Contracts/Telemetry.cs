namespace EventDrivenDesign.Contracts;

/// <summary>
/// Acknowledgement a producer publishes to <c>control.acks</c> after applying a command.
/// </summary>
/// <param name="CommandId">Id of the command being acknowledged.</param>
/// <param name="ServiceId">Id of the acknowledging producer.</param>
/// <param name="Accepted">Whether the command was applied.</param>
/// <param name="State">Producer state after applying the command.</param>
/// <param name="At">When the acknowledgement was produced.</param>
public sealed record CommandAck(string CommandId, string ServiceId, bool Accepted, string State, DateTimeOffset At);

/// <summary>
/// Per-second self-report every .NET service publishes to <c>telemetry.heartbeats</c>.
/// </summary>
/// <param name="ServiceId">Id of the reporting service.</param>
/// <param name="Kind">Service kind: Producer, Consumer or Api.</param>
/// <param name="State">Current lifecycle state (Booting, Idle, Running).</param>
/// <param name="At">When the heartbeat was produced.</param>
/// <param name="CpuPercent">Process CPU usage percent (spec section 8 formula).</param>
/// <param name="WorkingSetBytes">Process working set in bytes.</param>
/// <param name="GcHeapBytes">GC heap size in bytes.</param>
/// <param name="Threads">Managed thread count.</param>
/// <param name="StreamId">Producers: current stream; otherwise null.</param>
/// <param name="Generated">Producers: total events generated.</param>
/// <param name="Acked">Producers: total delivery reports acknowledged.</param>
/// <param name="Failed">Producers: total failed deliveries.</param>
/// <param name="Consumed">Consumers: total records consumed.</param>
/// <param name="Persisted">Consumers: total rows persisted.</param>
/// <param name="DeadLettered">Consumers: total records sent to the DLQ.</param>
/// <param name="Assigned">Consumers: assigned partition ids.</param>
public sealed record Heartbeat(
    string ServiceId,
    string Kind,
    string State,
    DateTimeOffset At,
    double CpuPercent,
    long WorkingSetBytes,
    long GcHeapBytes,
    int Threads,
    string? StreamId,
    long Generated,
    long Acked,
    long Failed,
    long Consumed,
    long Persisted,
    long DeadLettered,
    int[] Assigned);
