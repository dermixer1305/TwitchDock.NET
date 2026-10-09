using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;

namespace TwitchSdk.Authentication;

public sealed partial class TwitchOAuthClient
{
    private static readonly Uri Authority = new("https://id.twitch.tv/oauth2/");
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public TwitchOAuthClient(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _time = timeProvider ?? TimeProvider.System;
    }

    public static string CreateState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Compare the callback state to the single-use value saved in the initiating browser session.</summary>
    public static bool ValidateState(string? expected, string? actual)
        => !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual) &&
           CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    public static Uri CreateAuthorizationUri(string clientId, Uri redirectUri, IEnumerable<string> scopes, string state, bool forceVerify = false, bool implicitGrant = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(scopes);
        ValidateRedirect(redirectUri);
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = implicitGrant ? "token" : "code", ["scope"] = string.Join(' ', scopes),
            ["state"] = state, ["force_verify"] = forceVerify ? "true" : "false"
        };
        return BuildAuthorizeUri(values);
    }

    private static Uri BuildAuthorizeUri(Dictionary<string, string> values)
        => new(Authority, "authorize?" + string.Join('&', values.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")));

    public Task<OAuthTokenResponse> GetAppTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
        => TokenAsync(Credentials(clientId, clientSecret, "client_credentials"), cancellationToken);

    public Task<OAuthTokenResponse> ExchangeCodeAsync(string clientId, string clientSecret, string code, Uri redirectUri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ValidateRedirect(redirectUri);
        var form = Credentials(clientId, clientSecret, "authorization_code");
        form.Add("code", code);
        form.Add("redirect_uri", redirectUri.AbsoluteUri);
        return TokenAsync(form, cancellationToken);
    }

    public Task<OAuthTokenResponse> RefreshAsync(string clientId, string refreshToken, string? clientSecret = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        var form = new Dictionary<string, string> { ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken };
        if (!string.IsNullOrEmpty(clientSecret)) form.Add("client_secret", clientSecret);
        return TokenAsync(form, cancellationToken);
    }

    public Task<DeviceAuthorization> StartDeviceAuthorizationAsync(string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(scopes);
        return PostAsync("device", new Dictionary<string, string> { ["client_id"] = clientId, ["scopes"] = string.Join(' ', scopes) }, OAuthJsonContext.Default.DeviceAuthorization, cancellationToken);
    }

    public Task<OAuthTokenResponse> ExchangeDeviceCodeAsync(string clientId, string deviceCode, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        ArgumentNullException.ThrowIfNull(scopes);
        return TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["scopes"] = string.Join(' ', scopes)
        }, cancellationToken);
    }

    /// <summary>
    /// Polls the token endpoint at the advertised interval until the user authorizes the device code.
    /// Throws <see cref="TimeoutException"/> when the code expires and <see cref="TwitchApiException"/> when Twitch rejects it.
    /// </summary>
    public async Task<OAuthTokenResponse> WaitForDeviceAuthorizationAsync(string clientId, DeviceAuthorization authorization, IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopes);
        var scopeList = scopes.ToArray();
        var deadline = _time.GetUtcNow().AddSeconds(Math.Max(1, authorization.ExpiresIn));
        var interval = TimeSpan.FromSeconds(Math.Max(1, authorization.Interval));
        while (true)
        {
            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
            try { return await ExchangeDeviceCodeAsync(clientId, authorization.DeviceCode, scopeList, cancellationToken).ConfigureAwait(false); }
            catch (TwitchApiException ex) when (ex.Error == "authorization_pending") { }
            catch (TwitchApiException ex) when (ex.Error == "slow_down") { interval += TimeSpan.FromSeconds(5); }
            if (_time.GetUtcNow() >= deadline) throw new TimeoutException("The device code expired before the user authorized it.");
        }
    }

    public async Task<TokenValidation> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Authority, "validate"));
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, OAuthJsonContext.Default.TokenValidation, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string clientId, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["token"] = accessToken });
        using var response = await _http.PostAsync(new Uri(Authority, "revoke"), content, cancellationToken).ConfigureAwait(false);
        await ThrowIfErrorAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResponse> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var response = await PostAsync("token", form, OAuthJsonContext.Default.OAuthTokenResponse, ct).ConfigureAwait(false);
        response.Kind = form["grant_type"] == "client_credentials" ? TwitchTokenKind.App : TwitchTokenKind.User;
        response.ClientId = form["client_id"];
        return response;
    }

    private async Task<T> PostAsync<T>(string path, Dictionary<string, string> form, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(new Uri(Authority, path), content, ct).ConfigureAwait(false);
        return await ReadAsync(response, type, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        await ThrowIfErrorAsync(response, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false) ?? throw new JsonException("Empty OAuth response.");
    }

    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? error = null;
        // Only machine-readable error codes are retained; raw OAuth responses may contain credentials.
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                error = e.GetString();
            // Twitch reports some protocol states only in "message"; keep just the known machine-readable values.
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                error = KnownOAuthMessage(m.GetString()) ?? error;
        }
        catch (JsonException) { }
        throw new TwitchApiException(response.StatusCode, error, $"Twitch OAuth request failed with HTTP {(int)response.StatusCode}.");
    }

    private static string? KnownOAuthMessage(string? message) => message?.Trim().ToLowerInvariant() switch
    {
        "authorization_pending" => "authorization_pending",
        "slow_down" => "slow_down",
        "invalid device code" => "invalid_device_code",
        "invalid refresh token" => "invalid_refresh_token",
        "invalid access token" => "invalid_access_token",
        "invalid client" => "invalid_client",
        "invalid client secret" => "invalid_client_secret",
        "missing client secret" => "missing_client_secret",
        _ => null,
    };

    private static Dictionary<string, string> Credentials(string clientId, string clientSecret, string grant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        return new() { ["client_id"] = clientId, ["client_secret"] = clientSecret, ["grant_type"] = grant };
    }

    private static void ValidateRedirect(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!redirectUri.IsAbsoluteUri || !string.IsNullOrEmpty(redirectUri.Fragment)) throw new ArgumentException("An absolute registered redirect URI without a fragment is required.", nameof(redirectUri));
    }
}
