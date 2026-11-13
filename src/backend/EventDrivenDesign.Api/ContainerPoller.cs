using System.Net.Sockets;
using EventDrivenDesign.Contracts;

namespace EventDrivenDesign.Api;

/// <summary>
/// Reads the project's containers from the Docker Engine over its Unix socket, for the demo's container panel.
/// GET calls only: it never changes Docker. Any failure becomes <c>Available = false</c> with a reason,
/// logged once, so a missing socket never touches the other endpoints.
/// </summary>
public sealed class ContainerPoller(ILogger<ContainerPoller> log) : BackgroundService
{
    private const string SocketPath = "/var/run/docker.sock";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DfTimeout = TimeSpan.FromSeconds(20); // /system/df walks every layer and volume
    private static readonly string ListPath =
        "/containers/json?all=true&filters="
        + Uri.EscapeDataString("""{"label":["com.docker.compose.project=edd"]}""");

    private readonly HttpClient _docker = new(
        new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified
                );
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        }
    )
    {
        BaseAddress = new Uri("http://docker"),
        Timeout = DfTimeout,
    };

    private readonly Dictionary<string, CpuSample> _lastCpu = []; // only the poll loop touches it
    private volatile DiskUsage _disk = DiskUsage.Empty;
    private volatile ContainersView _latest = new(
        DateTimeOffset.UtcNow,
        false,
        "waiting for the first poll",
        []
    );

    /// <summary>The latest view; cheap to read from any thread.</summary>
    public ContainersView Latest => _latest;

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(
            Loop("containers", TimeSpan.FromSeconds(5), PollTimeout, PollAsync, ct),
            Loop(
                "docker-df",
                TimeSpan.FromSeconds(30),
                DfTimeout,
                async c =>
                    _disk = ContainerMath.ParseDiskUsage(
                        await _docker.GetStringAsync("/system/df", c)
                    ),
                ct
            )
        );

    private async Task PollAsync(CancellationToken ct)
    {
        var listed = ContainerMath.ParseList(await _docker.GetStringAsync(ListPath, ct));
        var stats = await Task.WhenAll(
            listed.Select(async c =>
                c.State == "running"
                    ? ContainerMath.ParseStats(
                        await _docker.GetStringAsync(
                            $"/containers/{c.Id}/stats?stream=false&one-shot=true",
                            ct
                        )
                    )
                    : null
            )
        );
        var disk = _disk;
        var views = listed
            .Select(
                (c, i) =>
                {
                    var s = stats[i];
                    double? cpu = null;
                    if (s?.Cpu is { } now)
                    {
                        cpu = ContainerMath.CpuPercent(_lastCpu.GetValueOrDefault(c.Id), now);
                        _lastCpu[c.Id] = now;
                    }
                    var (tier, order) = ContainerMath.TierOf(c.Service);
                    return new ContainerView(
                        c.Name,
                        c.Service,
                        tier,
                        order,
                        c.State,
                        c.Health,
                        c.Status,
                        c.Image,
                        Size(disk.ImageBytes, c.ImageId),
                        c.Ports,
                        cpu,
                        s?.MemoryUsed,
                        s?.MemoryLimit,
                        s?.NetRx,
                        s?.NetTx,
                        s?.BlockRead,
                        s?.BlockWrite,
                        Size(disk.WritableBytes, c.Id),
                        [
                            .. c.Volumes.Select(v => new ContainerVolume(
                                v.Name,
                                v.Destination,
                                Size(disk.VolumeBytes, v.Name)
                            )),
                        ],
                        null // shortcut: start time needs /containers/{id}/json; the panel shows Docker's status text instead
                    );
                }
            )
            .ToList();
        _latest = new ContainersView(DateTimeOffset.UtcNow, true, null, views);
    }

    private static long? Size(IReadOnlyDictionary<string, long> sizes, string key) =>
        sizes.TryGetValue(key, out var v) ? v : null;

    private Task Loop(
        string name,
        TimeSpan every,
        TimeSpan timeout,
        Func<CancellationToken, Task> poll,
        CancellationToken ct
    ) =>
        Task.Run(
            async () =>
            {
                using var timer = new PeriodicTimer(every);
                string? lastReason = null;
                do
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(timeout);
                        await poll(cts.Token);
                        lastReason = null;
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        var reason = e switch
                        {
                            OperationCanceledException =>
                                "the Docker Engine did not answer in time",
                            HttpRequestException or SocketException =>
                                "the Docker socket is not reachable",
                            _ => "the Docker Engine answer could not be read",
                        };
                        if (name == "containers")
                            _latest = new ContainersView(DateTimeOffset.UtcNow, false, reason, []);
                        if (reason != lastReason)
                            log.LogWarning(
                                "Container poll {Name} failed: {Reason} ({Error})",
                                name,
                                reason,
                                e.Message
                            );
                        lastReason = reason;
                    }
                } while (await Tick(timer, ct));
            },
            ct
        );

    private static async Task<bool> Tick(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public override void Dispose()
    {
        _docker.Dispose();
        base.Dispose();
    }
}
