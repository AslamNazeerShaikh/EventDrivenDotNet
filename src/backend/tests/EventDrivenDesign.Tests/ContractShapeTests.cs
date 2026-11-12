using System.Text.Json;
using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using Xunit;

namespace EventDrivenDesign.Tests;

public class ContractShapeTests
{
    [Fact]
    public void Reading_uses_camelCase_and_keeps_signal_names()
    {
        var r = new MachineReading(
            "P1-BDY-W042",
            "WeldRobot",
            "BDY",
            184223,
            DateTimeOffset.Parse("2026-11-12T10:15:03.120Z"),
            new Dictionary<string, double> { ["weld_current_ka"] = 11.8 }
        );
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(r, Json.Options));
        doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .Should()
            .Equal("machineId", "machineType", "shop", "seq", "deviceTime", "signals");
        doc.RootElement.GetProperty("signals")
            .GetProperty("weld_current_ka")
            .GetDouble()
            .Should()
            .Be(11.8);
        var back = JsonSerializer.Deserialize<MachineReading>(doc.RootElement, Json.Options)!;
        (back.MachineId, back.Seq, back.DeviceTime, back.Signals["weld_current_ka"])
            .Should()
            .Be((r.MachineId, r.Seq, r.DeviceTime, 11.8));
    }

    [Fact]
    public void InjectFault_has_type_discriminator() =>
        JsonSerializer
            .Serialize<GatewayCommand>(
                new InjectFault("P1-BDY-W042", AlarmKind.Overheat),
                Json.Options
            )
            .Should()
            .Be("""{"$type":"InjectFault","machineId":"P1-BDY-W042","kind":"Overheat"}""");

    [Fact]
    public void Pause_is_type_only() =>
        JsonSerializer
            .Serialize<GatewayCommand>(new Pause(), Json.Options)
            .Should()
            .Be("""{"$type":"Pause"}""");

    [Fact]
    public void Command_round_trips() =>
        (
            JsonSerializer.Deserialize<GatewayCommand>("""{"$type":"Resume"}""", Json.Options)
            is Resume
        )
            .Should()
            .BeTrue();

    [Fact]
    public void Unknown_command_type_throws()
    {
        var act = () =>
            JsonSerializer.Deserialize<GatewayCommand>("""{"$type":"Reboot"}""", Json.Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Degraded_health_has_type_and_reason() =>
        JsonSerializer
            .Serialize<ServiceHealth>(new Degraded("no update for 4 s"), Json.Options)
            .Should()
            .Be("""{"$type":"Degraded","reason":"no update for 4 s"}""");

    [Fact]
    public void Alarm_kind_is_a_string() =>
        JsonSerializer
            .Serialize(
                new MachineAlarm(
                    "P1-BDY-W042",
                    7,
                    184223,
                    DateTimeOffset.UnixEpoch,
                    AlarmKind.DataGap,
                    "warning",
                    null,
                    null,
                    null
                ),
                Json.Options
            )
            .Should()
            .Contain("\"kind\":\"DataGap\"");
}
