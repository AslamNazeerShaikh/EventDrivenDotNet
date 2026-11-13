using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Infrastructure;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var shop = config["Gateway:Shop"]
    ?? throw new InvalidOperationException("Gateway__Shop is required.");
var instanceId = config["Gateway:Instance"]
    ?? throw new InvalidOperationException("Gateway__Instance is required.");
var plant = config["Gateway:Plant"] ?? "P1";
var seed = config.GetValue<int>("Gateway:Seed");
var faultsPerMinute = config.GetValue("Gateway:FaultsPerMinute", 0.5);
var historyDepthSeconds = config.GetValue("Gateway:HistoryDepthSeconds", 600);
var bootstrap = config["Kafka:BootstrapServers"] ?? "plant-kafka:9092";
var bufferPath = config["Buffer:Path"] ?? "/data/buffer.db";
var machineIds = MachineCatalog.ForShop(plant, shop).Select(m => m.MachineId).ToArray();
var shopMachines = MachineCatalog.ForShop(plant, shop);

// Cross-term state shared by every whileActive invocation: the fence for a sender left by a previous term,
// and the singletons the term needs (resolved lazily, at the lease's construction, before any term can run).
var fence = new SenderFence();
var term = new TermServices();
GatewayLease? lease = null;

var whileActive = async (CancellationToken termToken, CancellationToken senderToken) =>
{
    var logger = term.Logger!;
    // 1. Fence: cancel any sender left by a previous term and wait for it to flush (bounded, 10 s). Reachable only after a lost session.
    var previousSender = fence.SenderTask;
    if (previousSender is { IsCompleted: false })
    {
        fence.SenderCts?.Cancel();
        using var fenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await previousSender.WaitAsync(fenceTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The previous term's sender did not flush within 10 s; carrying on");
        }
        fence.SenderCts?.Dispose();
        fence.SenderCts = null;
    }

    // 2. Reconcile with the plant log, on every term: the partner may have numbered further. A term's reconcile and a standby's reconcile never overlap.
    await lease!.StandbyGate.WaitAsync(termToken);
    try
    {
        await PlantLogReader.ReconcileBufferAsync(
            term.Buffer!,
            bootstrap,
            machineIds,
            logger,
            termToken
        );
    }
    finally
    {
        lease.StandbyGate.Release();
    }

    // 3. Resume numbering from the state on disk; the previous CollectorService has stopped by now.
    term.Collector!.Resume(term.Buffer!.LoadState());
    lease.MarkActive(); // only now does /metrics report this instance's numbering as the shop's

    // 4. Construct the collector, start it (its first act is the backfill), then start the sender.
    var svc = new CollectorService(
        term.Collector,
        term.Buffer,
        shopMachines,
        faultsPerMinute,
        historyDepthSeconds,
        TimeProvider.System
    );
    await svc.StartAsync(termToken);
    var senderCts = CancellationTokenSource.CreateLinkedTokenSource(senderToken);
    var senderTask = term.Sender!.RunAsync(senderCts.Token);
    fence.SenderCts = senderCts;
    fence.SenderTask = senderTask;

    // 5. Hold until the term is cancelled or a side faults; then stop the collector, drain the sender, dispose the collector.
    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var endRegistration = termToken.Register(() => ended.TrySetResult());
    void Observe(Task t, bool completeOnNormal) =>
        _ = t.ContinueWith(
            x =>
            {
                if (x.IsFaulted)
                    ended.TrySetException(x.Exception!.InnerException!);
                else if (x.IsCanceled || completeOnNormal)
                    ended.TrySetException(
                        new InvalidOperationException("a term side stopped unexpectedly")
                    );
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    Observe(svc.ExecuteTask!, completeOnNormal: true); // the collector never ends before the term does
    Observe(senderTask, completeOnNormal: false); // a normal stop after cancellation (the lost-session flush) is not fatal

    Exception? fatal = null;
    try
    {
        await ended.Task;
    }
    catch (Exception e)
    {
        fatal = e;
    }

    // No timeout: the start-up backfill ignores the stop token, and the next term's Resume or a standby
    // reconcile must not run beside it. It is bounded by the history depth (60 k rows: about 2 s).
    try
    {
        await svc.StopAsync(CancellationToken.None);
    }
    catch (Exception e)
    {
        fatal ??= e;
    }
    if (svc.ExecuteTask is { IsFaulted: true } collectorFault)
        fatal ??= collectorFault.Exception!.InnerException!;
    svc.Dispose(); // the manually constructed collector is not the container's to dispose
    try
    {
        await senderTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (TimeoutException)
    {
        logger.LogWarning("The sender did not drain within 10 s; unacknowledged rows stay on disk");
    }
    catch (Exception e)
    {
        fatal ??= e;
    }
    if (fatal is not null)
        throw fatal;
};

builder.Services.AddGlobalExceptionHandling();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, Json.Options.TypeInfoResolver!)
);

// The container owns the buffer and producer: it disposes them after the hosted services have stopped.
builder.Services.AddSingleton(_ => GatewayBuffer.Open(bufferPath));
builder.Services.AddSingleton(sp => new Collector(
    plant,
    shop,
    seed,
    sp.GetRequiredService<GatewayBuffer>().LoadState()
));
builder.Services.AddSingleton(_ =>
    new ProducerBuilder<string, byte[]>(KafkaConfigs.Producer(bootstrap)).Build()
);
builder.Services.AddSingleton(sp => new Sender(
    sp.GetRequiredService<GatewayBuffer>(),
    sp.GetRequiredService<IProducer<string, byte[]>>(),
    shop,
    TimeProvider.System
));
builder.Services.AddSingleton<GatewayControl>();
builder.Services.AddSingleton<ProcessSampler>();
builder.Services.AddSingleton(sp =>
{
    term.Buffer = sp.GetRequiredService<GatewayBuffer>();
    term.Collector = sp.GetRequiredService<Collector>();
    term.Sender = sp.GetRequiredService<Sender>();
    term.Logger = sp.GetRequiredService<ILogger<GatewayLease>>();
    lease = new GatewayLease(
        bootstrap,
        shop,
        instanceId,
        term.Buffer,
        machineIds,
        whileActive,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("gateway-lease")
    );
    return lease;
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayLease>());

var app = builder.Build();
var leaseService = app.Services.GetRequiredService<GatewayLease>();
app.UseGlobalExceptionHandling();

app.MapGet("/healthz", () => Results.Ok());

var sampler = app.Services.GetRequiredService<ProcessSampler>();
app.MapGet("/metrics", (Collector collector, GatewayBuffer buffer, Sender sender) =>
{
    var stats = buffer.Stats();
    ProcessStats process;
    lock (sampler)
        process = sampler.Sample(); // the sampler is not thread-safe
    var age = stats.OldestDeviceTimeMs is { } oldest
        ? Math.Max(0, TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds() - oldest)
        : 0;
    var seqs = collector.LastSeqByMachine;
    return Results.Json(
        new GatewayMetrics(
            instanceId,
            shop,
            sender.State.ToString(),
            buffer.LoadPaused(),
            seqs.Count,
            collector.Generated,
            stats.Readings,
            stats.Alarms,
            age,
            sender.SentPerSecond,
            sender.SendErrors,
            stats.FileBytes,
            seqs,
            process,
            leaseService.IsActive ? "active" : "standby"
        ),
        Json.Options
    );
});

app.MapPost(
    "/control",
    async (GatewayCommand command, GatewayControl control, CancellationToken ct) =>
        (await control.ApplyAsync(command, ct)).ToHttpResult()
);

app.Run();

internal sealed class SenderFence
{
    public CancellationTokenSource? SenderCts;
    public Task? SenderTask;
}

internal sealed class TermServices
{
    public GatewayBuffer? Buffer { get; set; }
    public Collector? Collector { get; set; }
    public Sender? Sender { get; set; }
    public ILogger<GatewayLease>? Logger { get; set; }
}
