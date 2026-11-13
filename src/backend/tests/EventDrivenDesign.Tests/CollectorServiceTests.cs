using AwesomeAssertions;
using EventDrivenDesign.Domain;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EventDrivenDesign.Tests;

public sealed class CollectorServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("colsvc").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private (GatewayBuffer Buffer, Collector Collector, CollectorService Service) New(
        double faultsPerMinute = 0
    )
    {
        var buffer = GatewayBuffer.Open(Path.Combine(_dir, "buffer.db"));
        var collector = new Collector("P1", "BDY", 7, buffer.LoadState());
        return (
            buffer,
            collector,
            new CollectorService(
                collector,
                buffer,
                MachineCatalog.ForShop("P1", "BDY"),
                faultsPerMinute,
                600,
                _time
            )
        );
    }

    private async Task TickOnceAsync(CollectorService service, CancellationToken ct)
    {
        await service.StartAsync(ct);
        await Task.Delay(100, ct); // let ExecuteAsync reach its first timer wait
        _time.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_tick_writes_one_reading_per_machine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (buffer, _, service) = New();
        await using var _ = buffer;

        await TickOnceAsync(service, ct);
        await Task.Delay(500, ct);
        await service.StopAsync(ct);

        buffer.Stats().Readings.Should().Be(100);
    }

    [Fact]
    public async Task A_paused_gateway_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (buffer, _, service) = New();
        await using var _ = buffer;
        await buffer.SetPausedAsync(true, ct);

        await TickOnceAsync(service, ct);
        await Task.Delay(500, ct);
        await service.StopAsync(ct);

        buffer.Stats().Readings.Should().Be(0);
    }

    [Fact]
    public async Task Start_backfills_before_the_first_tick()
    {
        var ct = TestContext.Current.CancellationToken;
        var (buffer, collector, service) = New();
        await using var _ = buffer;
        var t = _time.GetUtcNow();
        await buffer.WriteTickAsync(collector.Tick(t.AddSeconds(-120)), ct);

        await service.StartAsync(ct);
        await Task.Delay(500, ct); // the clock does not advance: only the backfill can write
        await service.StopAsync(ct);

        buffer.Stats().Readings.Should().Be(12_100);
        buffer.LoadState().Values.Should().OnlyContain(s => s.LastSeq == 121);
    }

    [Fact]
    public async Task A_paused_gateway_never_backfills()
    {
        var ct = TestContext.Current.CancellationToken;
        var (buffer, collector, service) = New();
        await using var _ = buffer;
        await buffer.WriteTickAsync(collector.Tick(_time.GetUtcNow().AddSeconds(-120)), ct);
        await buffer.SetPausedAsync(true, ct);

        await service.StartAsync(ct);
        await Task.Delay(500, ct);
        await service.StopAsync(ct);

        buffer.Stats().Readings.Should().Be(100);
    }

    [Fact]
    public async Task A_failed_buffer_write_is_fatal()
    {
        var ct = TestContext.Current.CancellationToken;
        var (buffer, _, service) = New();
        await buffer.DisposeAsync(); // every later write now throws

        await TickOnceAsync(service, ct);

        await FluentActions
            .Awaiting(() => service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), ct))
            .Should()
            .ThrowAsync<Exception>();
        service.ExecuteTask!.IsFaulted.Should().BeTrue();
    }
}
