using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchSdk.EventSub;

namespace TwitchSdk.Tests;

internal static class EventSubContractAssertions
{
    /// <summary>Round-trips a fixture event (keyed type@version) through its typed model, keeping nulls so omitted model fields fail.</summary>
    public static TEvent Verify<TEvent>(string file, EventSubEventDefinition<TEvent> definition) where TEvent : class
    {
        using var expected = JsonDocument.Parse(ContractAssertions.Fixture(file, definition.ToString()));
        var model = definition.Deserialize(expected.RootElement);
        var context = new EventSubEventsJsonContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never });
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(model, context.GetTypeInfo(typeof(TEvent))!));
        ContractAssertions.AssertSubset(expected.RootElement, actual.RootElement);
        return model;
    }
}
