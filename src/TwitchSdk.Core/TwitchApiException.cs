using System.Net;

namespace TwitchSdk.Core;

public sealed class TwitchApiException(HttpStatusCode statusCode, string? error, string message, string? requestId = null, string? existingSubscriptionId = null)
    : HttpRequestException(message, null, statusCode)
{
    public string? Error { get; } = error;
    public string? RequestId { get; } = requestId;
    /// <summary>The existing EventSub subscription ID returned with a duplicate-subscription conflict.</summary>
    public string? ExistingSubscriptionId { get; } = existingSubscriptionId;
}
