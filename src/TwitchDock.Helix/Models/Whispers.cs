using System.Text.Json.Serialization;

namespace TwitchDock.Helix.Models;

public sealed class SendWhisperRequest
{
    [JsonIgnore]
    public string FromUserId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public string ToUserId { get; init => field = value ?? ""; } = "";
    public required string Message { get; init; }
}
