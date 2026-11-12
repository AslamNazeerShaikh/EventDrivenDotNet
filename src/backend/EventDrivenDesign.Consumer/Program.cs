using Confluent.Kafka;
using EventDrivenDesign.Consumer;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Infrastructure;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var consumerId = config["Consumer:Id"] ?? throw new InvalidOperationException("Consumer__Id is required.");
var maxRowsPerSecond = config.GetValue("Consumer:MaxRowsPerSecond", 1000);
var bootstrap = config["Kafka:BootstrapServers"] ?? "central-kafka:9092";
var connectionString = config["ConnectionStrings:Central"] ?? "Host=central-db;Username=edd;Password=edd;Database=edd";

builder.Services.AddGlobalExceptionHandling();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, Json.Options.TypeInfoResolver!));

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton(sp => new CentralStore(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton(_ => new ConsumerStats(TimeProvider.System));
builder.Services.AddSingleton<ProcessSampler>();

builder.Services.AddStoreWriterLoops(bootstrap, consumerId, maxRowsPerSecond);

var app = builder.Build();
app.UseGlobalExceptionHandling();

app.MapGet("/healthz", () => Results.Ok());

var sampler = app.Services.GetRequiredService<ProcessSampler>();
app.MapGet("/metrics", (ConsumerStats stats, BatchLoop<MachineReading> readings) =>
{
    ProcessStats process;
    lock (sampler) process = sampler.Sample(); // the sampler is not thread-safe
    return Results.Json(new ConsumerMetrics(
        consumerId, readings.Assignment, stats.ConsumedRate.PerSecond, stats.InsertedRate.PerSecond,
        stats.Received, stats.Inserted, stats.Rejected, stats.LastBatchMs, process), Json.Options);
});

app.Run();
