using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

public static class Json
{
    public static JsonSerializerOptions Options { get; } =
        EventDrivenDesignJsonContext.Default.Options;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(MachineReading))]
[JsonSerializable(typeof(MachineAlarm))]
[JsonSerializable(typeof(GatewayCommand))]
[JsonSerializable(typeof(Pause))]
[JsonSerializable(typeof(Resume))]
[JsonSerializable(typeof(InjectFault))]
[JsonSerializable(typeof(GatewayMetrics))]
[JsonSerializable(typeof(ConsumerMetrics))]
[JsonSerializable(typeof(ProcessStats))]
[JsonSerializable(typeof(ServiceHealth))]
[JsonSerializable(typeof(Healthy))]
[JsonSerializable(typeof(Degraded))]
[JsonSerializable(typeof(Down))]
[JsonSerializable(typeof(GatewayTile))]
[JsonSerializable(typeof(ClusterTile))]
[JsonSerializable(typeof(ConsumerTile))]
[JsonSerializable(typeof(DatabaseTile))]
[JsonSerializable(typeof(Overview))]
[JsonSerializable(typeof(MachineProof))]
[JsonSerializable(typeof(Reconciliation))]
[JsonSerializable(typeof(ChartPoint))]
[JsonSerializable(typeof(Chart))]
[JsonSerializable(typeof(AlarmView))]
[JsonSerializable(typeof(IReadOnlyList<AlarmView>))]
internal sealed partial class EventDrivenDesignJsonContext : JsonSerializerContext;
