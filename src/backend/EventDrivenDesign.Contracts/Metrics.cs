namespace EventDrivenDesign.Contracts;

public sealed record GatewayMetrics(
    string GatewayId,
    string Shop,
    string State,
    bool Paused,
    int Machines,
    long Generated,
    long BufferedReadings,
    long BufferedAlarms,
    long OldestUnsentAgeMs,
    double SentPerSecond,
    long SendErrors,
    long BufferBytes,
    IReadOnlyDictionary<string, long> LastSeqByMachine,
    ProcessStats Process
);

public sealed record ConsumerMetrics(
    string ConsumerId,
    int[] AssignedPartitions,
    double ConsumedPerSecond,
    double InsertedPerSecond,
    long Received,
    long Inserted,
    long Rejected,
    long LastBatchMs,
    ProcessStats Process
);

public sealed record ProcessStats(double CpuPercent, long WorkingSetBytes, long GcHeapBytes);
