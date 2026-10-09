using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Tests;

public sealed class SerializationTests
{
    [Theory]
    [InlineData("""{"expires_at":""}""", null)]
    [InlineData("""{"expires_at":null}""", null)]
    [InlineData("""{}""", null)]
    [InlineData("""{"expires_at":"2026-10-09T12:00:00.123456789Z"}""", "2026-10-09T12:00:00.1234567Z")]
    public void EmptyTimestampStringsReadAsNull(string json, string? expected)
    {
        var model = JsonSerializer.Deserialize(json, SerializationTestContext.Default.TimestampHolder)!;
        Assert.Equal(expected is null ? null : DateTimeOffset.Parse(expected), model.ExpiresAt);
    }

    [Fact]
    public void EmptyTimestampConverterRejectsNonStringsAndWritesNull()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"expires_at":5}""", SerializationTestContext.Default.TimestampHolder));
        Assert.Equal("""{"expires_at":null}""", JsonSerializer.Serialize(new TimestampHolder(), SerializationTestContext.Default.TimestampHolder));
    }
}

public sealed class TimestampHolder
{
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TimestampHolder))]
internal partial class SerializationTestContext : JsonSerializerContext;
