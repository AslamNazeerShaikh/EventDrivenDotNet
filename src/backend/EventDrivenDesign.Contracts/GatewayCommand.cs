using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

// Control-room commands to a gateway, over HTTP. Exhaustive switch at the one call site.
public sealed record Pause;

public sealed record Resume;

public sealed record InjectFault(string MachineId, AlarmKind Kind);

// Built-in union serialisation cannot disambiguate the overlapping object shapes,
// so the wire form carries a $type discriminator via GatewayCommandConverter.
[JsonConverter(typeof(GatewayCommandConverter))]
public union GatewayCommand(Pause, Resume, InjectFault);

public sealed class GatewayCommandConverter : JsonConverter<GatewayCommand>
{
    public override GatewayCommand Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("GatewayCommand must be a JSON object.");
        }

        string? kind = doc.RootElement.TryGetProperty("$type", out JsonElement t)
            ? t.GetString()
            : null;
        return kind switch
        {
            "Pause" => (GatewayCommand)doc.RootElement.Deserialize<Pause>(options)!,
            "Resume" => (GatewayCommand)doc.RootElement.Deserialize<Resume>(options)!,
            "InjectFault" => (GatewayCommand)doc.RootElement.Deserialize<InjectFault>(options)!,
            _ => throw new JsonException($"Unknown GatewayCommand $type: {kind}."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        GatewayCommand value,
        JsonSerializerOptions options
    )
    {
        (string kind, object active) = value switch
        {
            Pause p => ("Pause", (object)p),
            Resume r => ("Resume", r),
            InjectFault f => ("InjectFault", f),
            null => throw new JsonException("Cannot serialize a null GatewayCommand."),
        };
        writer.WriteStartObject();
        writer.WriteString("$type", kind);
        foreach (
            JsonProperty prop in JsonSerializer
                .SerializeToElement(active, active.GetType(), options)
                .EnumerateObject()
        )
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
