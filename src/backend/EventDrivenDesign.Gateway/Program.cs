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
var plant = config["Gateway:Plant"] ?? "P1";
var seed = config.GetValue<int>("Gateway:Seed");
var faultsPerMinute = config.GetValue("Gateway:FaultsPerMinute", 0.5);
var bootstrap = config["Kafka:BootstrapServers"] ?? "plant-kafka:9092";
var bufferPath = config["Buffer:Path"] ?? "/data/buffer.db";

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
builder.Services.AddHostedService<SenderService>();
builder.Services.AddHostedService(sp => new CollectorService(
    sp.GetRequiredService<Collector>(),
    sp.GetRequiredService<GatewayBuffer>(),
    MachineCatalog.ForShop(plant, shop),
    faultsPerMinute,
    TimeProvider.System
));

var app = builder.Build();
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
            $"gateway-{shop.ToLowerInvariant()}",
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
            process
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
