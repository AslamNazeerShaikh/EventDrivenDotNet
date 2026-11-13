namespace EventDrivenDesign.Contracts;

public sealed record GatewayTile(
    string Shop,
    ServiceHealth Health,
    string State,
    bool Paused,
    double SentPerSecond,
    long BufferedReadings,
    long BufferedAlarms,
    long OldestUnsentAgeMs,
    long Generated,
    string? Instance // the shop's active instance; null when the shop has none
);

public sealed record ClusterTile(
    ServiceHealth Health,
    long EndOffsetTotal,
    DateTimeOffset? NewestDeviceTime
);

public sealed record ConsumerTile(
    string ConsumerId,
    ServiceHealth Health,
    double InsertedPerSecond,
    long Received,
    long Inserted
);

public sealed record DatabaseTile(ServiceHealth Health, long Stored, double? FreshnessSeconds);

public sealed record Overview(
    DateTimeOffset At,
    IReadOnlyList<GatewayTile> Gateways,
    ClusterTile PlantKafka,
    double? MirrorLagSeconds,
    ClusterTile CentralKafka,
    long ConsumerLag,
    IReadOnlyList<ConsumerTile> Consumers,
    DatabaseTile Database,
    double LiveRate,
    double DrainRate,
    double? DrainEtaSeconds
);

public sealed record MachineProof(
    string MachineId,
    long Generated,
    long Stored,
    long Missing,
    long Gaps,
    long Over // max(0, MaxSeq − generated): a second writer numbered beyond what the active instance generated
);

// Verdict is one of "PASS", "WAIT", "FAIL".
public sealed record Reconciliation(
    long Generated,
    long Stored,
    long Missing,
    long Gaps,
    long DuplicatesAbsorbed,
    long? DuplicateRows,
    bool Paused,
    bool Drained,
    string Verdict,
    IReadOnlyList<MachineProof> Machines,
    long Over
);

public sealed record ChartPoint(DateTimeOffset BucketStart, long Readings, bool Provisional);

public sealed record Chart(DateTimeOffset? Watermark, IReadOnlyList<ChartPoint> Points);

public sealed record AlarmView(
    string MachineId,
    long AlarmSeq,
    AlarmKind Kind,
    string Severity,
    DateTimeOffset DeviceTime,
    DateTimeOffset StoredAt,
    double LatencySeconds
);

// The project's containers as the Docker Engine reports them, for the demo's container panel.
// Available is false, with a short Reason, when the engine cannot be read; the other endpoints are unaffected.
public sealed record ContainersView(
    DateTimeOffset At,
    bool Available,
    string? Reason,
    IReadOnlyList<ContainerView> Containers
);

// State is Docker's ("running", "exited"); Health is "healthy", "unhealthy", "starting" or null; Status is
// Docker's text ("Up 5 minutes (healthy)"). TierOrder is the tier's place in the data flow, plant to control room.
public sealed record ContainerView(
    string Name,
    string Service,
    string Tier,
    int TierOrder,
    string State,
    string? Health,
    string Status,
    string Image,
    long? ImageBytes,
    IReadOnlyList<ContainerPort> Ports,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryLimitBytes,
    long? NetRxBytes,
    long? NetTxBytes,
    long? BlockReadBytes,
    long? BlockWriteBytes,
    long? WritableBytes,
    IReadOnlyList<ContainerVolume> Volumes,
    DateTimeOffset? StartedAt
);

// Public is null when the port is not published on the host.
public sealed record ContainerPort(int Private, int? Public, string Type);

public sealed record ContainerVolume(string Name, string Destination, long? Bytes);
