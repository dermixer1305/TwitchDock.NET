using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchDock.Helix.Models;

/// <summary>
/// Reads a Unix timestamp in seconds, where 0 means "no time", as a nullable <see cref="DateTimeOffset"/>.
/// Also accepts an RFC3339 string or an empty string, which older Twitch documentation described. Writes Unix seconds (0 for null).
/// </summary>
internal sealed class UnixSecondsDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                var seconds = reader.GetInt64();
                return seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds);
            case JsonTokenType.String:
                var text = reader.GetString();
                return string.IsNullOrEmpty(text) ? null : reader.GetDateTimeOffset();
            default:
                throw new JsonException("Expected a Unix timestamp in seconds.");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value?.ToUnixTimeSeconds() ?? 0);
    }
}
