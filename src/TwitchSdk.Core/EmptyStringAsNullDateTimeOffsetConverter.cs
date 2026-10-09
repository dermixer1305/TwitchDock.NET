using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchSdk.Core;

/// <summary>
/// Reads timestamps that Twitch sends as an empty string instead of null (for example permanent bans).
/// Apply with <c>[JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]</c> on a <c>DateTimeOffset?</c> property.
/// </summary>
public sealed class EmptyStringAsNullDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a timestamp string.");
        if (!reader.ValueIsEscaped && reader.ValueSpan.IsEmpty) return null;
        if (reader.ValueIsEscaped && reader.GetString()!.Length == 0) return null;
        return reader.GetDateTimeOffset();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } timestamp) writer.WriteStringValue(timestamp);
        else writer.WriteNullValue();
    }
}
