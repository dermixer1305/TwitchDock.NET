using System.Text.Json.Serialization;

namespace TwitchSdk.Helix.Models;

public sealed class SendWhisperRequest
{
    [JsonIgnore]
    public string FromUserId { get; init; } = "";
    [JsonIgnore]
    public string ToUserId { get; init; } = "";
    public required string Message { get; init; }
}
