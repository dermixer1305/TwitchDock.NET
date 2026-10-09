namespace TwitchSdk.Authentication;

/// <summary>An OAuth redirect reported an error, carried a mismatched state or lacked the expected values.</summary>
public sealed class TwitchOAuthCallbackException(string message, string? error = null, string? errorDescription = null) : Exception(message)
{
    /// <summary>The OAuth error code, for example access_denied when the user declined.</summary>
    public string? Error { get; } = error;
    public string? ErrorDescription { get; } = errorDescription;
}

public sealed class AuthorizationCodeCallback
{
    public required string Code { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public override string ToString() => "AuthorizationCodeCallback [redacted]";
}

public sealed class ImplicitGrantCallback
{
    public required string AccessToken { get; init; }
    /// <summary>Present when the openid scope was requested with an id_token response type.</summary>
    public string? IdToken { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public string TokenType { get; init; } = "bearer";
    public override string ToString() => "ImplicitGrantCallback [redacted]";
}

/// <summary>
/// Parses Twitch OAuth redirects. The state is compared in constant time before any other value is trusted;
/// consume the stored state once, whatever the outcome.
/// </summary>
public static class TwitchOAuthCallbacks
{
    /// <summary>Reads the authorization code from the redirect query string.</summary>
    public static AuthorizationCodeCallback ParseAuthorizationCode(Uri callbackUri, string expectedState)
    {
        var query = Parse(callbackUri, expectedState, fragment: false);
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new TwitchOAuthCallbackException("The redirect did not contain an authorization code.");
        return new() { Code = code, Scopes = SplitScopes(query.GetValueOrDefault("scope")) };
    }

    /// <summary>Reads the access token (and optional ID token) from the redirect fragment of the implicit grant.</summary>
    public static ImplicitGrantCallback ParseImplicitGrant(Uri callbackUri, string expectedState)
    {
        var fragment = Parse(callbackUri, expectedState, fragment: true);
        var accessToken = fragment.GetValueOrDefault("access_token");
        var idToken = fragment.GetValueOrDefault("id_token");
        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(idToken))
            throw new TwitchOAuthCallbackException("The redirect did not contain a token.");
        return new()
        {
            AccessToken = accessToken ?? "", IdToken = idToken, Scopes = SplitScopes(fragment.GetValueOrDefault("scope")),
            TokenType = fragment.GetValueOrDefault("token_type") ?? "bearer",
        };
    }

    private static Dictionary<string, string> Parse(Uri callbackUri, string expectedState, bool fragment)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        if (!callbackUri.IsAbsoluteUri) throw new ArgumentException("An absolute redirect URI is required.", nameof(callbackUri));
        var query = Decode(callbackUri.Query);
        // Twitch reports denials in the query string for both flows.
        var values = fragment && !query.ContainsKey("error") ? Decode(callbackUri.Fragment) : query;
        if (!TwitchOAuthClient.ValidateState(expectedState, values.GetValueOrDefault("state")))
            throw new TwitchOAuthCallbackException("The OAuth state does not match the value stored for this session.");
        if (values.TryGetValue("error", out var error))
            throw new TwitchOAuthCallbackException("Twitch did not grant authorization.", error, values.GetValueOrDefault("error_description"));
        return values;
    }

    private static Dictionary<string, string> Decode(string component)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in component.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Unescape(separator < 0 ? pair : pair[..separator]);
            var value = separator < 0 ? "" : Unescape(pair[(separator + 1)..]);
            // A repeated parameter is ambiguous and may indicate tampering.
            if (!values.TryAdd(name, value)) throw new TwitchOAuthCallbackException($"The redirect repeats the {name} parameter.");
        }
        return values;
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static IReadOnlyList<string> SplitScopes(string? scope)
        => scope is null ? [] : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
