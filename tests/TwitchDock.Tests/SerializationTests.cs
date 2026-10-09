using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchDock.Core;

namespace TwitchDock.Tests;

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
    public void MissingJsonFieldsKeepPropertyDefaults()
    {
        // The STJ source generator assigns init-only properties at construction, passing null for absent fields.
        var page = JsonSerializer.Deserialize("{}", TwitchDock.Helix.HelixJsonContext.Default.HelixPageCustomReward)!;
        Assert.NotNull(page.Data);
        var reward = JsonSerializer.Deserialize("""{"data":[{"broadcaster_id":"1","broadcaster_login":"a","broadcaster_name":"A","id":"r","title":"t","default_image":{"url_1x":"a","url_2x":"b","url_4x":"c"},"background_color":"#fff"}]}""",
            TwitchDock.Helix.HelixJsonContext.Default.HelixPageCustomReward)!.Data.Single();
        Assert.Equal("", reward.Prompt);
        Assert.NotNull(reward.MaxPerStreamSetting);
    }

    [Fact]
    public void EveryNonNullableInitPropertyCoalescesNullToItsDefault()
    {
        var nullability = new NullabilityInfoContext();
        var failures = new List<string>();
        var assemblies = new[] { typeof(AccessToken), typeof(TwitchDock.Helix.HelixClient), typeof(TwitchDock.EventSub.EventSubMessage), typeof(TwitchDock.Authentication.TwitchOAuthClient) }.Select(t => t.Assembly);
        foreach (var type in assemblies.SelectMany(a => a.GetExportedTypes()).Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && t.GetConstructor(Type.EmptyTypes) is not null))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var setter = property.SetMethod;
                if (setter is null || property.PropertyType.IsValueType || property.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() is not null) continue;
                if (!setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))) continue;
                if (nullability.Create(property).WriteState != NullabilityState.NotNull) continue;
                var instance = Activator.CreateInstance(type)!;
                property.SetValue(instance, null);
                if (property.GetValue(instance) is null) failures.Add($"{type.Name}.{property.Name}");
            }
        }
        Assert.Empty(failures);
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
