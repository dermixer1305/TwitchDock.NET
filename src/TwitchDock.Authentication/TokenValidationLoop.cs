using TwitchDock.Core;

namespace TwitchDock.Authentication;

public static class TokenValidationLoop
{
    /// <summary>Validates immediately and hourly, including when idle. Any failure ends the loop and must be handled by the host.</summary>
    public static async Task RunAsync(TwitchOAuthClient oauth, IAccessTokenProvider provider, string expectedClientId,
        Func<TokenValidation, CancellationToken, Task> onValidated, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedClientId);
        ArgumentNullException.ThrowIfNull(onValidated);
        var time = timeProvider ?? TimeProvider.System;
        var refreshedAfterRejection = false;
        while (true)
        {
            var token = await provider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            TokenValidation result;
            try { result = await oauth.ValidateAsync(token.Value, cancellationToken).ConfigureAwait(false); }
            catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized && !refreshedAfterRejection)
            {
                // Twitch invalidated the token (for example after a password change). Try one refresh before giving up.
                var next = await provider.RefreshTokenAsync(token, cancellationToken).ConfigureAwait(false);
                if (next.Value == token.Value) throw;
                refreshedAfterRejection = true;
                continue;
            }
            refreshedAfterRejection = false;
            if (!string.Equals(result.ClientId, expectedClientId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Twitch token belongs to a different client ID.");
            if (provider is ITokenMetadataSink sink)
            {
                if (!await sink.UpdateMetadataAsync(result.ToAccessToken(token.Value, time), cancellationToken).ConfigureAwait(false))
                    continue; // The validated token rotated concurrently; validate its replacement before notifying the host.
            }
            await onValidated(result, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromHours(1), time, cancellationToken).ConfigureAwait(false);
        }
    }
}
