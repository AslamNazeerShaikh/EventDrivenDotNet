namespace EventDrivenDesign.Contracts;

public sealed record GatewayTile(string Shop, ServiceHealth Health, string State, bool Paused, double SentPerSecond, long BufferedReadings, long BufferedAlarms, long OldestUnsentAgeMs, long Generated);

public sealed record ClusterTile(ServiceHealth Health, long EndOffsetTotal, DateTimeOffset? NewestDeviceTime);

public sealed record ConsumerTile(string ConsumerId, ServiceHealth Health, double InsertedPerSecond, long Received, long Inserted);

public sealed record DatabaseTile(ServiceHealth Health, long Stored, double? FreshnessSeconds);

public sealed record Overview(DateTimeOffset At, IReadOnlyList<GatewayTile> Gateways, ClusterTile PlantKafka, double? MirrorLagSeconds, ClusterTile CentralKafka, long ConsumerLag, IReadOnlyList<ConsumerTile> Consumers, DatabaseTile Database, double LiveRate, double DrainRate, double? DrainEtaSeconds);

public sealed record MachineProof(string MachineId, long Generated, long Stored, long Missing, long Gaps);

// Verdict is one of "PASS", "WAIT", "FAIL".
public sealed record Reconciliation(long Generated, long Stored, long Missing, long Gaps, long DuplicatesAbsorbed, long? DuplicateRows, bool Paused, bool Drained, string Verdict, IReadOnlyList<MachineProof> Machines);

public sealed record ChartPoint(DateTimeOffset BucketStart, long Readings, bool Provisional);

public sealed record Chart(DateTimeOffset? Watermark, IReadOnlyList<ChartPoint> Points);

public sealed record AlarmView(string MachineId, long AlarmSeq, AlarmKind Kind, string Severity, DateTimeOffset DeviceTime, DateTimeOffset StoredAt, double LatencySeconds);
