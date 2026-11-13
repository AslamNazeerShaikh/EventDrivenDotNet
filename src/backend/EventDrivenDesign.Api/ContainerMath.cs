using System.Text.Json;
using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Api;

/// <summary>The CPU counters of one stats sample, in nanoseconds.</summary>
public sealed record CpuSample(long Total, long System, int OnlineCpus);

/// <summary>One container's figures from a one-shot <c>/containers/{id}/stats</c> call.</summary>
public sealed record ContainerStats(
    CpuSample? Cpu,
    long? MemoryUsed,
    long? MemoryLimit,
    long? NetRx,
    long? NetTx,
    long? BlockRead,
    long? BlockWrite
);

/// <summary>The sizes from <c>/system/df</c>: images by id, writable layers by container id, volumes by name.</summary>
public sealed record DiskUsage(
    IReadOnlyDictionary<string, long> ImageBytes,
    IReadOnlyDictionary<string, long> WritableBytes,
    IReadOnlyDictionary<string, long> VolumeBytes
)
{
    public static readonly DiskUsage Empty = new(
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>()
    );
}

/// <summary>A named volume mounted into a container.</summary>
public sealed record MountedVolume(string Name, string Destination);

/// <summary>One entry of <c>/containers/json</c>, with only the fields the panel needs.</summary>
public sealed record ListedContainer(
    string Id,
    string Name,
    string Service,
    string State,
    string? Health,
    string Status,
    string Image,
    string ImageId,
    IReadOnlyList<ContainerPort> Ports,
    IReadOnlyList<MountedVolume> Volumes
);

/// <summary>Pure parsing and arithmetic for the container panel, kept apart from the socket so it can be tested.</summary>
public static class ContainerMath
{
    /// <summary>CPU % between two samples, like <c>docker stats</c>; null without a usable previous sample.</summary>
    public static double? CpuPercent(CpuSample? previous, CpuSample now)
    {
        if (previous is null)
            return null;
        var system = now.System - previous.System;
        var total = now.Total - previous.Total;
        if (system <= 0 || total < 0 || now.OnlineCpus <= 0)
            return null;
        return (double)total / system * now.OnlineCpus * 100;
    }

    /// <summary>Used memory without the reclaimable file cache, as <c>docker stats</c> shows it.</summary>
    public static long? MemoryUsed(long? usage, long? inactiveFile) =>
        usage is { } u ? u - (inactiveFile ?? 0) : null;

    /// <summary>The tier of a Compose service and its place in the data flow, plant to control room.</summary>
    public static (string Tier, int Order) TierOf(string service) =>
        service switch
        {
            _ when service.StartsWith("gateway-", StringComparison.Ordinal) => ("Edge gateways", 1),
            "plant-kafka" or "plant-topics" => ("Plant Kafka", 2),
            _ when service.StartsWith("mm2-", StringComparison.Ordinal) => ("MirrorMaker 2", 3),
            "central-kafka" => ("Central Kafka", 4),
            _ when service.StartsWith("consumer-", StringComparison.Ordinal) => (
                "Store writers",
                5
            ),
            "central-db" => ("PostgreSQL", 6),
            "dashboard" => ("Control room", 7),
            _ when service.StartsWith("api-", StringComparison.Ordinal) => ("Control room", 7),
            _ => ("Other", 8),
        };

    /// <summary>The health in Docker's status text: "Up 5 minutes (healthy)" → healthy; null without a health check.</summary>
    public static string? HealthOf(string status) =>
        status.Contains("(healthy)", StringComparison.Ordinal) ? "healthy"
        : status.Contains("(unhealthy)", StringComparison.Ordinal) ? "unhealthy"
        : status.Contains("(health: starting)", StringComparison.Ordinal) ? "starting"
        : null;

    public static ContainerStats ParseStats(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        CpuSample? cpu = null;
        if (
            root.TryGetProperty("cpu_stats", out var c)
            && Long(c, "system_cpu_usage") is { } system
            && c.TryGetProperty("cpu_usage", out var usage)
            && Long(usage, "total_usage") is { } total
        )
            cpu = new CpuSample(total, system, (int)(Long(c, "online_cpus") ?? 0));

        long? memUsed = null,
            memLimit = null;
        if (root.TryGetProperty("memory_stats", out var m))
        {
            long? inactive =
                m.TryGetProperty("stats", out var ms) && ms.ValueKind == JsonValueKind.Object
                    ? Long(ms, "inactive_file") ?? Long(ms, "total_inactive_file")
                    : null;
            memUsed = MemoryUsed(Long(m, "usage"), inactive);
            memLimit = Long(m, "limit");
        }

        long? rx = null,
            tx = null;
        if (root.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
            foreach (var n in nets.EnumerateObject())
            {
                rx = (rx ?? 0) + (Long(n.Value, "rx_bytes") ?? 0);
                tx = (tx ?? 0) + (Long(n.Value, "tx_bytes") ?? 0);
            }

        long? read = null,
            write = null;
        if (
            root.TryGetProperty("blkio_stats", out var b)
            && b.TryGetProperty("io_service_bytes_recursive", out var io)
            && io.ValueKind == JsonValueKind.Array
        )
            foreach (var e in io.EnumerateArray())
            {
                var op = e.TryGetProperty("op", out var o) ? o.GetString() : null;
                if (string.Equals(op, "read", StringComparison.OrdinalIgnoreCase))
                    read = (read ?? 0) + (Long(e, "value") ?? 0);
                else if (string.Equals(op, "write", StringComparison.OrdinalIgnoreCase))
                    write = (write ?? 0) + (Long(e, "value") ?? 0);
            }

        return new ContainerStats(cpu, memUsed, memLimit, rx, tx, read, write);
    }

    public static DiskUsage ParseDiskUsage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DiskUsage(
            Sizes(root, "Images", "Id", e => Long(e, "Size")),
            Sizes(root, "Containers", "Id", e => Long(e, "SizeRw")),
            Sizes(root, "Volumes", "Name", e =>
                e.TryGetProperty("UsageData", out var u) && u.ValueKind == JsonValueKind.Object
                    ? Long(u, "Size")
                    : null
            )
        );
    }

    public static IReadOnlyList<ListedContainer> ParseList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return
        [
            .. doc
                .RootElement.EnumerateArray()
                .Select(e =>
                {
                    var status = Str(e, "Status") ?? "";
                    var name =
                        e.TryGetProperty("Names", out var names) && names.GetArrayLength() > 0
                            ? names[0].GetString()!.TrimStart('/')
                            : Str(e, "Id")!;
                    var service =
                        e.TryGetProperty("Labels", out var labels)
                        && labels.ValueKind == JsonValueKind.Object
                            ? Str(labels, "com.docker.compose.service") ?? name
                            : name;
                    return new ListedContainer(
                        Str(e, "Id")!,
                        name,
                        service,
                        Str(e, "State") ?? "unknown",
                        HealthOf(status),
                        status,
                        Str(e, "Image") ?? "",
                        Str(e, "ImageID") ?? "",
                        Items(e, "Ports")
                            .Select(p => new ContainerPort(
                                (int)(Long(p, "PrivatePort") ?? 0),
                                (int?)Long(p, "PublicPort"),
                                Str(p, "Type") ?? "tcp"
                            ))
                            .Distinct() // IPv4 and IPv6 bindings of one port
                            .ToList(),
                        Items(e, "Mounts")
                            .Where(v => Str(v, "Type") == "volume")
                            .Select(v => new MountedVolume(
                                Str(v, "Name") ?? "",
                                Str(v, "Destination") ?? ""
                            ))
                            .ToList()
                    );
                }),
        ];
    }

    private static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : [];

    private static Dictionary<string, long> Sizes(
        JsonElement root,
        string array,
        string key,
        Func<JsonElement, long?> size
    )
    {
        var result = new Dictionary<string, long>();
        foreach (var e in Items(root, array))
            if (Str(e, key) is { } k && size(e) is { } s && s >= 0) // the engine sends -1 for a size not yet computed
                result[k] = s;
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : null;
}
