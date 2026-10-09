using System.Net;

namespace TwitchSdk.Core;

public sealed class TwitchApiException(HttpStatusCode statusCode, string? error, string message, string? requestId = null, string? existingSubscriptionId = null,
    TimeSpan? retryAfter = null)
    : HttpRequestException(message, null, statusCode)
{
    public string? Error { get; } = error;
    public string? RequestId { get; } = requestId;
    /// <summary>The existing EventSub subscription ID returned with a duplicate-subscription conflict.</summary>
    public string? ExistingSubscriptionId { get; } = existingSubscriptionId;
    /// <summary>
    /// For a Helix HTTP 429 that TwitchHttpClient did not (or no longer) retry: how long Twitch asked to wait, from Retry-After
    /// or Ratelimit-Reset. Null when Twitch sent neither header, the status is not 429, or the error came from the OAuth client.
    /// </summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
