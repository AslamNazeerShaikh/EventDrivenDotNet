using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Domain;

public sealed record ProgressRow(
    string MachineId,
    long Stored,
    long MaxSeq,
    DateTimeOffset MaxDeviceTime
);

/// <summary>The arithmetic behind the proof panel and the drill verdict. Pure: no I/O, no clock.</summary>
public static class Proof
{
    public static Reconciliation Reconcile(
        IReadOnlyDictionary<string, long> generated,
        IReadOnlyList<ProgressRow> progress,
        long duplicatesAbsorbed,
        long? duplicateRows,
        bool allPaused,
        bool drained
    )
    {
        var rows = progress.ToDictionary(p => p.MachineId);
        var machines = generated
            .Keys.Union(rows.Keys)
            .Order(StringComparer.Ordinal)
            .Select(id =>
            {
                var g = generated.GetValueOrDefault(id);
                rows.TryGetValue(id, out var row);
                var stored = row?.Stored ?? 0;
                // A machine unknown to `generated` would go negative and cancel real losses elsewhere.
                return new MachineProof(
                    id,
                    g,
                    stored,
                    Math.Max(0, g - stored),
                    row is null ? 0 : row.MaxSeq - stored,
                    row is null || !generated.ContainsKey(id) ? 0 : Math.Max(0, row.MaxSeq - g)
                );
            })
            .ToList();

        var missing = machines.Sum(m => m.Missing);
        var gaps = machines.Sum(m => m.Gaps);
        var over = machines.Sum(m => m.Over);
        var verdict =
            !(allPaused && drained) ? "WAIT"
            : missing == 0 && gaps == 0 && over == 0 ? "PASS"
            : "FAIL";

        return new Reconciliation(
            generated.Values.Sum(),
            machines.Sum(m => m.Stored),
            missing,
            gaps,
            duplicatesAbsorbed,
            duplicateRows,
            allPaused,
            drained,
            verdict,
            machines,
            over
        );
    }

    public static double? DrainEtaSeconds(long backlog, double drainRate, double liveRate) =>
        backlog == 0 ? 0
        : drainRate <= liveRate ? null
        : backlog / (drainRate - liveRate);

    public static DateTimeOffset? Watermark(IReadOnlyList<ProgressRow> progress) =>
        progress
            .Where(p => p.Stored == p.MaxSeq)
            .Select(p => (DateTimeOffset?)p.MaxDeviceTime)
            .Min();

    public static ServiceHealth HealthFromAge(TimeSpan age, DateTimeOffset lastSeen) =>
        age < TimeSpan.FromSeconds(3) ? new Healthy()
        : age < TimeSpan.FromSeconds(10) ? new Degraded($"no update for {(int)age.TotalSeconds} s")
        : new Down(lastSeen);
}
