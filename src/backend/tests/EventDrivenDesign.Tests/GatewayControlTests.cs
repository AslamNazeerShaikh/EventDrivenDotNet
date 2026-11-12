using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Gateway;
using EventDrivenDesign.Persistence;
using EventDrivenDesign.Shared;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EventDrivenDesign.Tests;

public sealed class GatewayControlTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
    private readonly string _dir = Directory.CreateTempSubdirectory("gwctl").FullName;
    private GatewayBuffer _buffer = null!;
    private Collector _collector = null!;
    private GatewayControl _control = null!;

    public ValueTask InitializeAsync()
    {
        _buffer = GatewayBuffer.Open(Path.Combine(_dir, "buffer.db"));
        _collector = new Collector("P1", "BDY", 7, _buffer.LoadState());
        _control = new GatewayControl(_collector, _buffer);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _buffer.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Inject_valid_fault_succeeds_and_reaches_the_collector()
    {
        var result = await _control.ApplyAsync(new InjectFault("P1-BDY-W042", AlarmKind.Overheat), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _collector.Tick(T0).Rows.Should().ContainSingle(r => r.Lane == Lane.Alarm);
    }

    [Fact]
    public async Task Unknown_machine_is_not_found()
    {
        var result = await _control.ApplyAsync(new InjectFault("P1-BDY-X999", AlarmKind.Overheat), TestContext.Current.CancellationToken);

        result.Error.Should().Be(new Error("gateway.machine.unknown", result.Error!.Message, ErrorKind.NotFound));
    }

    [Fact]
    public async Task Inapplicable_fault_is_invalid()
    {
        var result = await _control.ApplyAsync(new InjectFault("P1-BDY-W042", AlarmKind.EStop), TestContext.Current.CancellationToken);

        result.Error.Should().Be(new Error("gateway.fault.not_applicable", result.Error!.Message, ErrorKind.Invalid));
    }

    [Fact]
    public async Task Pause_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _control.ApplyAsync(new Pause(), ct)).IsSuccess.Should().BeTrue();
        (await _control.ApplyAsync(new Pause(), ct)).IsSuccess.Should().BeTrue();

        _buffer.LoadPaused().Should().BeTrue();
    }

    [Fact]
    public async Task Resume_after_pause_unpauses()
    {
        var ct = TestContext.Current.CancellationToken;
        await _control.ApplyAsync(new Pause(), ct);

        (await _control.ApplyAsync(new Resume(), ct)).IsSuccess.Should().BeTrue();

        _buffer.LoadPaused().Should().BeFalse();
    }
}
