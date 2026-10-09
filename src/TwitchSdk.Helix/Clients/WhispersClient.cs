using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class WhispersClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>HTTP 204 does not guarantee delivery: Twitch may silently drop a whisper or truncate its text.</summary>
    public Task SendWhisperAsync(SendWhisperRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FromUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToUserId);
        ArgumentException.ThrowIfNullOrEmpty(request.Message);
        if (request.FromUserId == request.ToUserId) throw new ArgumentException("Sender and recipient must be different.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, "whispers",
            new HelixQuery().AddValue("from_user_id", request.FromUserId).AddValue("to_user_id", request.ToUserId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SendWhisperRequest),
            authorization: new([TwitchScopes.UserManageWhispers], requiredUserId: request.FromUserId), cancellationToken: cancellationToken);
    }
}
