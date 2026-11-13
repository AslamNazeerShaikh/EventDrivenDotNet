using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EventDrivenDesign.Api;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Infrastructure;
using EventDrivenDesign.Persistence;
using EventDrivenDesign.Shared;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
string[] Split(string key, string fallback) =>
    (config[key] ?? fallback).Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );
var gateways = Split(
    "Api:Gateways",
    "gateway-prs-a:8080,gateway-prs-b:8080,gateway-bdy-a:8080,gateway-bdy-b:8080,gateway-pnt-a:8080,gateway-pnt-b:8080,gateway-mch-a:8080,gateway-mch-b:8080,gateway-asm-a:8080,gateway-asm-b:8080"
);
var consumers = Split("Api:Consumers", "consumer-1:8080,consumer-2:8080");
var plantBootstrap = config["Kafka:Plant"] ?? "plant-kafka:9092";
var centralBootstrap = config["Kafka:Central"] ?? "central-kafka:9092";
var connectionString = config["ConnectionStrings:Central"]
    ?? "Host=central-db;Username=edd;Password=edd;Database=edd";
var gatewayInstances = gateways.Select(g => g.Split(':')[0]).ToList();

builder.Services.AddGlobalExceptionHandling();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, Json.Options.TypeInfoResolver!);
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // for request bodies outside the contracts, e.g. { "kind": "Overheat" }
});

builder.Services.AddSingleton(
    new Snapshot(gatewayInstances, consumers.Select(c => c.Split(':')[0]))
);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton(sp => new CentralQueries(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton(_ => new HttpClient(
    new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }
)
{
    Timeout = TimeSpan.FromSeconds(3),
});
builder.Services.AddSingleton(sp =>
{
    var snapshot = sp.GetRequiredService<Snapshot>();
    var loggers = sp.GetRequiredService<ILoggerFactory>();
    return new Pollers(
        snapshot,
        sp.GetRequiredService<HttpClient>(),
        sp.GetRequiredService<CentralQueries>(),
        new KafkaWatcher(
            plantBootstrap,
            TopicNames.Readings,
            null,
            snapshot.SeenPlant,
            loggers.CreateLogger("plant-kafka")
        ),
        new KafkaWatcher(
            centralBootstrap,
            "plant1." + TopicNames.Readings,
            "central-readings",
            snapshot.SeenCentral,
            loggers.CreateLogger("central-kafka")
        ),
        gateways,
        consumers,
        TimeProvider.System,
        loggers.CreateLogger<Pollers>()
    );
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<Pollers>());

var app = builder.Build();
app.UseGlobalExceptionHandling();

app.MapGet("/healthz", () => Results.Ok());

app.MapGet("/api/overview", (Snapshot s) =>
    Results.Json(OverviewBuilder.Build(s, TimeProvider.System.GetUtcNow()), Json.Options)
);

app.MapGet(
    "/api/reconciliation",
    async (bool? full, Snapshot s, Pollers pollers, CentralQueries queries, CancellationToken ct) =>
    {
        // Fresh progress rows, so the verdict never rests on a sample older than the offsets. A down database keeps the old sample, which then reads as not drained.
        try
        {
            await pollers.RefreshDbAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        long? duplicateRows = null;
        // The scan reads every row (16 M after a day: about 6 s), so it gets 60 s; it runs only on demand or when drained.
        if (full == true)
            duplicateRows = (await Db(queries.DuplicateRowsAsync, ct, TimeSpan.FromSeconds(60)))
                is { IsSuccess: true } r
                ? r.Value
                : null;
        return Results.Json(
            OverviewBuilder.Reconcile(s, TimeProvider.System.GetUtcNow(), duplicateRows),
            Json.Options
        );
    }
);

app.MapGet(
    "/api/chart",
    async (int? minutes, Snapshot s, CentralQueries queries, CancellationToken ct) =>
    {
        var buckets = await Db(c => queries.ChartAsync(Math.Clamp(minutes ?? 10, 1, 180), c), ct);
        var watermark = s.Db is { } db ? Proof.Watermark(db.Value.Progress) : null;
        return buckets.Match(
            b => Results.Json(OverviewBuilder.BuildChart(b, watermark), Json.Options),
            e => Failure(e)
        );
    }
);

app.MapGet("/api/alarms", async (CentralQueries queries, CancellationToken ct) =>
    (await Db(c => queries.RecentAlarmsAsync(50, c), ct)).ToHttpResult()
);

app.MapPost("/api/plant/pause", (Snapshot s, HttpClient http, CancellationToken ct) =>
    FanOut(new Pause(), s, http, ct)
);
app.MapPost("/api/plant/resume", (Snapshot s, HttpClient http, CancellationToken ct) =>
    FanOut(new Resume(), s, http, ct)
);

app.MapPost(
    "/api/machines/{machineId}/faults",
    async (
        string machineId,
        FaultRequest body,
        HttpClient http,
        Snapshot s,
        CancellationToken ct
    ) =>
    {
        var gateway = ApiRoutes.GatewayFor(machineId, s);
        if (gateway.Error is { } bad)
            return Failure(bad);
        try
        {
            using var response = await http.PostAsJsonAsync(
                $"http://{gateway.Value}/control",
                (GatewayCommand)new InjectFault(machineId, body.Kind),
                Json.Options,
                ct
            );
            if ((int)response.StatusCode == 400 || (int)response.StatusCode == 404)
                return Results.Content(
                    await response.Content.ReadAsStringAsync(ct),
                    "application/problem+json",
                    statusCode: (int)response.StatusCode
                );
            return response.IsSuccessStatusCode
                ? Results.StatusCode(StatusCodes.Status202Accepted)
                : Failure(
                    new Error(
                        "api.gateway.unavailable",
                        $"{gateway.Value} answered {(int)response.StatusCode}.",
                        ErrorKind.Unavailable
                    )
                );
        }
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Failure(
                new Error(
                    "api.gateway.unavailable",
                    $"{gateway.Value} is unreachable.",
                    ErrorKind.Unavailable
                )
            );
        }
    }
);

app.Run();

static IResult Failure(Error e) => Result.Failure(e).ToHttpResult();

// A query against the central database: bounded by a timeout, and a down database is a 503, not a 500.
static async Task<Result<T>> Db<T>(
    Func<CancellationToken, Task<T>> query,
    CancellationToken ct,
    TimeSpan? timeout = null
)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
    try
    {
        return await query(cts.Token);
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        return new Error(
            "api.database.unavailable",
            "The central database did not answer.",
            ErrorKind.Unavailable
        );
    }
}

// Pause and resume fan out to every configured instance, so a standby that takes over keeps the paused state.
// The error lists the instances not reached. Idempotent at the gateway: a retry after a partial failure is safe.
static async Task<IResult> FanOut(
    GatewayCommand command,
    Snapshot s,
    HttpClient http,
    CancellationToken ct
)
{
    var instances = s.Instances;
    var outcomes = await Task.WhenAll(
        instances.Select(async instance =>
        {
            try
            {
                using var response = await http.PostAsJsonAsync(
                    $"http://{instance}:8080/control",
                    command,
                    Json.Options,
                    ct
                );
                return response.IsSuccessStatusCode;
            }
            catch (Exception e)
                when (e is HttpRequestException or TaskCanceledException
                    && !ct.IsCancellationRequested
                )
            {
                return false;
            }
        })
    );
    var failed = instances.Where((_, i) => !outcomes[i]).ToList();
    return failed.Count == 0
        ? Results.StatusCode(StatusCodes.Status202Accepted)
        : Failure(
            new Error(
                "api.gateway.unavailable",
                $"Not reached: {string.Join(", ", failed)}.",
                ErrorKind.Unavailable
            )
        );
}

internal sealed record FaultRequest(AlarmKind Kind);
