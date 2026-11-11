using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

/// <summary>Starts a producer run at the given rate.</summary>
/// <param name="CommandId">Unique id of the command; also becomes the run id.</param>
/// <param name="RatePerSecond">Events to emit per second.</param>
/// <param name="MaxEvents">Optional cap after which the run completes.</param>
public sealed record Start(string CommandId, int RatePerSecond, long? MaxEvents);

/// <summary>Stops the current producer run.</summary>
/// <param name="CommandId">Unique id of the command.</param>
public sealed record Stop(string CommandId);

/// <summary>Changes the rate of a running producer.</summary>
/// <param name="CommandId">Unique id of the command.</param>
/// <param name="RatePerSecond">New events-per-second rate.</param>
public sealed record SetRate(string CommandId, int RatePerSecond);

/// <summary>
/// Commands the API sends to producers over <c>control.commands</c>.
/// Handled with an exhaustive switch; every variant must be covered.
/// Built-in union serialisation cannot disambiguate the overlapping object
/// shapes, so the wire form carries a <c>$type</c> discriminator via
/// <see cref="ProducerCommandConverter"/> (pinned by <c>ContractShapeTests</c>).
/// </summary>
[JsonConverter(typeof(ProducerCommandConverter))]
public union ProducerCommand(Start, Stop, SetRate);

/// <summary>
/// JSON converter for <see cref="ProducerCommand"/> writing a <c>$type</c>
/// discriminator alongside the case properties.
/// </summary>
public sealed class ProducerCommandConverter : JsonConverter<ProducerCommand>
{
    /// <summary>
    /// Reads a command by dispatching on its <c>$type</c> property.
    /// </summary>
    /// <param name="reader">The reader positioned at the command value.</param>
    /// <param name="typeToConvert">The union type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The deserialized command.</returns>
    /// <exception cref="JsonException">Thrown for a missing or unknown <c>$type</c>.</exception>
    public override ProducerCommand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        string? kind = doc.RootElement.TryGetProperty("$type", out JsonElement t) ? t.GetString() : null;
        return kind switch
        {
            "Start" => (ProducerCommand)doc.RootElement.Deserialize<Start>(options)!,
            "Stop" => (ProducerCommand)doc.RootElement.Deserialize<Stop>(options)!,
            "SetRate" => (ProducerCommand)doc.RootElement.Deserialize<SetRate>(options)!,
            _ => throw new JsonException($"Unknown ProducerCommand $type: {kind}."),
        };
    }

    /// <summary>
    /// Writes the active case with a leading <c>$type</c> property.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The command to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <exception cref="JsonException">Thrown for a null command value.</exception>
    public override void Write(Utf8JsonWriter writer, ProducerCommand value, JsonSerializerOptions options)
    {
        object active = value switch
        {
            Start s => (object)s,
            Stop s => (object)s,
            SetRate r => (object)r,
            null => throw new JsonException("Cannot serialize a null ProducerCommand."),
        };
        string kind = active switch { Start => "Start", Stop => "Stop", _ => "SetRate" };
        writer.WriteStartObject();
        writer.WriteString("$type", kind);
        foreach (JsonProperty prop in JsonSerializer.SerializeToElement(active, options).EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
