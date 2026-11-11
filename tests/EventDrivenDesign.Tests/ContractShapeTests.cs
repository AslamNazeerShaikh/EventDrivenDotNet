using System.Text.Json;
using EventDrivenDesign.Contracts;
using Xunit;

namespace EventDrivenDesign.Tests;

/// <summary>
/// Pins the exact wire shape of the shared contracts from spec section 5.
/// </summary>
public sealed class ContractShapeTests
{
    /// <summary>
    /// Verifies the topic names match the spec exactly.
    /// </summary>
    [Fact]
    public void TopicNames_MatchSpec()
    {
        Assert.Equal("demo.events", TopicNames.Events);
        Assert.Equal("demo.events.dlq", TopicNames.EventsDlq);
        Assert.Equal("control.commands", TopicNames.ControlCommands);
        Assert.Equal("control.acks", TopicNames.ControlAcks);
        Assert.Equal("telemetry.heartbeats", TopicNames.TelemetryHeartbeats);
    }

    /// <summary>
    /// Verifies a Start command round-trips through JSON with exact spec field names
    /// and a $type discriminator (built-in union serialisation is ambiguous for
    /// overlapping object shapes, so the union carries a custom converter).
    /// </summary>
    [Fact]
    public void StartCommand_JsonRoundTrip_PreservesShape()
    {
        ProducerCommand original = new Start("cmd-1", 200, null);

        string json = JsonSerializer.Serialize(original, EventDrivenDesignJsonContext.Default.ProducerCommand);
        Assert.Contains("\"$type\":\"Start\"", json);
        Assert.Contains("\"CommandId\":\"cmd-1\"", json);
        Assert.Contains("\"RatePerSecond\":200", json);

        ProducerCommand back = JsonSerializer.Deserialize(json, EventDrivenDesignJsonContext.Default.ProducerCommand);
        Assert.Equal(original, back);
    }

    /// <summary>
    /// Verifies Stop and SetRate commands round-trip through JSON.
    /// </summary>
    /// <param name="command">The command to round-trip.</param>
    [Theory]
    [MemberData(nameof(CommandCases))]
    public void ProducerCommands_JsonRoundTrip(ProducerCommand command)
    {
        string json = JsonSerializer.Serialize(command, EventDrivenDesignJsonContext.Default.ProducerCommand);
        ProducerCommand back = JsonSerializer.Deserialize(json, EventDrivenDesignJsonContext.Default.ProducerCommand);
        Assert.Equal(command, back);
    }

    /// <summary>
    /// Provides one instance of each command variant.
    /// </summary>
    /// <returns>Stop and SetRate commands.</returns>
    public static TheoryData<ProducerCommand> CommandCases() => new()
    {
        new Stop("cmd-2"),
        new SetRate("cmd-3", 500),
    };

    /// <summary>
    /// Verifies each command variant dispatches to its own arm of an exhaustive
    /// switch with no default arm (checked by the compiler for union types).
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    [Theory]
    [MemberData(nameof(CommandCases))]
    public void ProducerCommand_Switch_DispatchesEachVariant(ProducerCommand command)
    {
        string result = command switch
        {
            Start s => $"start:{s.RatePerSecond}",
            Stop s => $"stop:{s.CommandId}",
            SetRate r => $"rate:{r.RatePerSecond}",
            null => throw new InvalidOperationException("Null command."),
        };
        Assert.NotEmpty(result);
    }

    /// <summary>
    /// Verifies health states round-trip through JSON.
    /// </summary>
    /// <param name="health">The health value to round-trip.</param>
    [Theory]
    [MemberData(nameof(HealthCases))]
    public void ServiceHealth_JsonRoundTrip(ServiceHealth health)
    {
        string json = JsonSerializer.Serialize(health, EventDrivenDesignJsonContext.Default.ServiceHealth);
        ServiceHealth? back = JsonSerializer.Deserialize(json, EventDrivenDesignJsonContext.Default.ServiceHealth);
        Assert.Equal(health, back);
    }

    /// <summary>
    /// Provides one instance of each health state.
    /// </summary>
    /// <returns>Healthy, Degraded and Down values.</returns>
    public static TheoryData<ServiceHealth> HealthCases() => new()
    {
        new Healthy(),
        new Degraded("no heartbeat for 3 s"),
        new Down(new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero)),
    };

    /// <summary>
    /// Verifies command ack and heartbeat records round-trip through JSON.
    /// </summary>
    [Fact]
    public void AckAndHeartbeat_JsonRoundTrip()
    {
        var ack = new CommandAck("cmd-1", "producer-a", true, "Running", DateTimeOffset.UtcNow);
        string ackJson = JsonSerializer.Serialize(ack, EventDrivenDesignJsonContext.Default.CommandAck);
        Assert.Contains("\"CommandId\":\"cmd-1\"", ackJson);
        Assert.Equal(ack, JsonSerializer.Deserialize(ackJson, EventDrivenDesignJsonContext.Default.CommandAck));

        var heartbeat = new Heartbeat(
            "producer-a", "Producer", "Running", DateTimeOffset.UtcNow,
            12.5, 1024, 512, 8, "stream-1",
            1000, 999, 0, 0, 0, 0, [0, 1]);
        string hbJson = JsonSerializer.Serialize(heartbeat, EventDrivenDesignJsonContext.Default.Heartbeat);
        Assert.Contains("\"ServiceId\":\"producer-a\"", hbJson);
        Heartbeat? hbBack = JsonSerializer.Deserialize(hbJson, EventDrivenDesignJsonContext.Default.Heartbeat);
        Assert.NotNull(hbBack);
        Assert.Equal(heartbeat with { Assigned = [] }, hbBack with { Assigned = [] });
        Assert.Equal(heartbeat.Assigned, hbBack.Assigned);
    }
}
