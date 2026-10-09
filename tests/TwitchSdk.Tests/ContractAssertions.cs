using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using TwitchSdk.Helix;

namespace TwitchSdk.Tests;

internal static class ContractAssertions
{
    public static string Fixture(string file, string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        return document.RootElement.GetProperty(id).GetRawText();
    }

    public static void Verify(string file, string id, JsonTypeInfo type)
    {
        var fixture = Fixture(file, id);
        var model = JsonSerializer.Deserialize(fixture, type);
        // Include null properties so an accidentally omitted nullable model field cannot pass a round-trip check.
        var context = new HelixJsonContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never });
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(model, context.GetTypeInfo(type.Type)!));
        using var expected = JsonDocument.Parse(fixture);
        AssertSubset(expected.RootElement, actual.RootElement);
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coverage.json")));
        var endpoint = matrix.RootElement.GetProperty("helix").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        var fixtureNames = AllNames(expected.RootElement).ToHashSet(StringComparer.Ordinal);
        foreach (var field in endpoint.GetProperty("responseBody").EnumerateArray()) Assert.Contains(field.GetProperty("name").GetString()!, fixtureNames);
    }

    private static IEnumerable<string> AllNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) { yield return property.Name; foreach (var name in AllNames(property.Value)) yield return name; }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var name in AllNames(item)) yield return name;
    }

    internal static void AssertSubset(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind == JsonValueKind.Object)
            foreach (var property in expected.EnumerateObject()) { Assert.True(actual.TryGetProperty(property.Name, out var value), $"Missing {property.Name}"); AssertSubset(property.Value, value); }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
            for (var i = 0; i < expected.GetArrayLength(); i++) AssertSubset(expected[i], actual[i]);
        }
        else if (expected.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(TruncateToTicks(expected.GetString()!), out var date) && DateTimeOffset.TryParse(actual.GetString(), out var actualDate)) Assert.Equal(date, actualDate);
        else Assert.Equal(expected.ToString(), actual.ToString());
    }

    // System.Text.Json truncates fractional seconds beyond the 100 ns tick, whereas DateTimeOffset.Parse rounds them.
    private static string TruncateToTicks(string value) => Regex.Replace(value, @"(T\d{2}:\d{2}:\d{2}\.\d{7})\d+", "$1");
}
