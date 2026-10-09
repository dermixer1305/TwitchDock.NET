using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TwitchDock.EventSub.Events;

/// <summary>
/// Reads the v1 AutoMod message as the documented object or as the plain string shown in Twitch's v1 examples (then without fragments).
/// Always writes the object form.
/// </summary>
internal sealed class AutomodHeldMessageV1Converter : JsonConverter<AutomodHeldMessage>
{
    public override AutomodHeldMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String
            ? new AutomodHeldMessage { Text = reader.GetString()! }
            : JsonSerializer.Deserialize(ref reader, TypeInfo(options));

    public override void Write(Utf8JsonWriter writer, AutomodHeldMessage value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, TypeInfo(options));

    // The type-level contract carries no converter, so this does not recurse.
    private static JsonTypeInfo<AutomodHeldMessage> TypeInfo(JsonSerializerOptions options)
        => (JsonTypeInfo<AutomodHeldMessage>)options.GetTypeInfo(typeof(AutomodHeldMessage));
}
