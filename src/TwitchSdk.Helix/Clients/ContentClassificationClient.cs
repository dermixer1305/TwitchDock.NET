using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

/// <summary>Content classification labels (CCLs) that broadcasters apply through Modify Channel Information.</summary>
public sealed class ContentClassificationClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Accepts an app or user token. Locale such as de-DE is optional; Twitch defaults to en-US.</summary>
    public Task<HelixPage<ContentClassificationLabel>> GetContentClassificationLabelsAsync(string? locale = null, CancellationToken cancellationToken = default)
    {
        if (locale is not null) ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return _transport.SendAsync(HttpMethod.Get, "content_classification_labels", HelixJsonContext.Default.HelixPageContentClassificationLabel,
            new HelixQuery().AddValue("locale", locale), authorization: new([], allowAppToken: true), cancellationToken: cancellationToken);
    }
}
