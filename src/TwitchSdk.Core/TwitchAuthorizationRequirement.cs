namespace TwitchSdk.Core;

/// <summary>Preflight checks for known metadata. Twitch remains authoritative for grants, resource ownership and roles.</summary>
public sealed class TwitchAuthorizationRequirement
{
    public TwitchAuthorizationRequirement(IEnumerable<string> requiredUserScopes, bool allowAppToken = false, string? requiredUserId = null, bool allowUserToken = true,
        IEnumerable<string>? anyUserScopes = null)
    {
        ArgumentNullException.ThrowIfNull(requiredUserScopes);
        RequiredUserScopes = Array.AsReadOnly(requiredUserScopes.Distinct(StringComparer.Ordinal).ToArray());
        AllowAppToken = allowAppToken;
        RequiredUserId = requiredUserId;
        AllowUserToken = allowUserToken;
        AnyUserScopes = Array.AsReadOnly((anyUserScopes ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }
    public IReadOnlyList<string> RequiredUserScopes { get; }
    /// <summary>When nonempty, at least one of these scopes is required in addition to RequiredUserScopes.</summary>
    public IReadOnlyList<string> AnyUserScopes { get; }
    public bool AllowAppToken { get; }
    public bool AllowUserToken { get; }
    public string? RequiredUserId { get; }

    private const string NoOAuthToken = "This operation does not accept OAuth tokens; it requires a different credential such as an Extension JWT.";

    public void Validate(AccessToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Kind == TwitchTokenKind.App && !AllowAppToken)
            throw new TwitchAuthorizationException(AllowUserToken ? "This operation requires a user access token." : NoOAuthToken);
        if (token.Kind == TwitchTokenKind.User && !AllowUserToken)
            throw new TwitchAuthorizationException(AllowAppToken ? "This operation requires an app access token." : NoOAuthToken);
        if (token.Kind != TwitchTokenKind.User) return;
        if (RequiredUserId is not null && token.UserId is not null && RequiredUserId != token.UserId)
            throw new TwitchAuthorizationException("The user ID does not match the authenticated user.");
        if (!token.ScopesKnown) return;
        var missing = RequiredUserScopes.Where(scope => !token.Scopes.Contains(scope, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0) throw new TwitchAuthorizationException("The user token is missing required scopes.", missing);
        if (AnyUserScopes.Count > 0 && !AnyUserScopes.Any(scope => token.Scopes.Contains(scope, StringComparer.Ordinal)))
            throw new TwitchAuthorizationException("The user token requires at least one alternative scope.", requiredAnyOfScopes: AnyUserScopes);
    }
}

public sealed class TwitchAuthorizationException : InvalidOperationException
{
    public TwitchAuthorizationException(string message, IEnumerable<string>? missingScopes = null, IEnumerable<string>? requiredAnyOfScopes = null) : base(message)
    {
        MissingScopes = Array.AsReadOnly((missingScopes ?? []).ToArray());
        RequiredAnyOfScopes = Array.AsReadOnly((requiredAnyOfScopes ?? []).ToArray());
    }
    public IReadOnlyList<string> MissingScopes { get; }
    public IReadOnlyList<string> RequiredAnyOfScopes { get; }
}
