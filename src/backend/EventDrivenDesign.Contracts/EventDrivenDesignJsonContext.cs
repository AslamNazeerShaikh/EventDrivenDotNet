using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

/// <summary>
/// Source-generated JSON serialization context for the contract types.
/// Property names stay PascalCase to match the spec field names exactly.
/// </summary>
[JsonSerializable(typeof(Start))]
[JsonSerializable(typeof(Stop))]
[JsonSerializable(typeof(SetRate))]
[JsonSerializable(typeof(ProducerCommand))]
[JsonSerializable(typeof(ServiceHealth))]
[JsonSerializable(typeof(CommandAck))]
[JsonSerializable(typeof(Heartbeat))]
public partial class EventDrivenDesignJsonContext : JsonSerializerContext
{
}
