using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;

namespace TwitchDock.Authentication;

public sealed partial class TwitchOAuthClient
{
    private static readonly Uri Authority = new("https://id.twitch.tv/oauth2/");
    /// <summary>OAuth responses are small; larger bodies fail with <see cref="InvalidDataException"/>.</summary>
    private const int MaxResponseBytes = 1024 * 1024;
    /// <summary>RFC 8628 defaults for a device authorization response without expires_in or interval.</summary>
    private const int DefaultDeviceCodeLifetimeSeconds = 1800;
    private const int DefaultDevicePollingIntervalSeconds = 5;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly OpenIdSigningKeyCache _signingKeys;

    /// <param name="httpClient">Sends the OAuth requests; it is not disposed.</param>
    /// <param name="timeProvider">Clock for device polling and ID token lifetimes.</param>
    /// <param name="signingKeyCache">OpenID signing keys; defaults to <see cref="OpenIdSigningKeyCache.Shared"/>.</param>
    public TwitchOAuthClient(HttpClient httpClient, TimeProvider? timeProvider = null, OpenIdSigningKeyCache? signingKeyCache = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _time = timeProvider ?? TimeProvider.System;
        _signingKeys = signingKeyCache ?? OpenIdSigningKeyCache.Shared;
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
        // A missing (zero) expires_in or interval must not turn into a one-second deadline or a request storm.
        var deadline = _time.GetUtcNow().AddSeconds(authorization.ExpiresIn > 0 ? authorization.ExpiresIn : DefaultDeviceCodeLifetimeSeconds);
        var interval = TimeSpan.FromSeconds(authorization.Interval > 0 ? authorization.Interval : DefaultDevicePollingIntervalSeconds);
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
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, OAuthJsonContext.Default.TokenValidation, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string clientId, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Authority, "revoke"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["token"] = accessToken }),
        };
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ThrowIfErrorAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns after the headers so that <see cref="ReadBodyAsync"/> can bound the body instead of HttpClient buffering it.</summary>
    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

    private async Task<OAuthTokenResponse> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var response = await PostAsync("token", form, OAuthJsonContext.Default.OAuthTokenResponse, ct).ConfigureAwait(false);
        response.Kind = form["grant_type"] == "client_credentials" ? TwitchTokenKind.App : TwitchTokenKind.User;
        response.ClientId = form["client_id"];
        return response;
    }

    private async Task<T> PostAsync<T>(string path, Dictionary<string, string> form, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Authority, path)) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        return await ReadAsync(response, type, ct).ConfigureAwait(false);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        await ThrowIfErrorAsync(response, ct).ConfigureAwait(false);
        var body = await ReadBodyAsync(response.Content, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body.Span, type) ?? throw new JsonException("Empty OAuth response.");
    }

    /// <summary>
    /// Reads at most 1 MiB, whether or not the response declares its length. HttpClient.Timeout ends when the headers arrive
    /// (ResponseHeadersRead), so the same timeout is applied to the body and surfaces like HttpClient's own timeout.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) throw ResponseTooLarge();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_http.Timeout);
        try
        {
            await using var stream = await content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw ResponseTooLarge();
                buffer.Write(chunk, 0, read);
            }
            return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            var message = $"The Twitch OAuth response body did not arrive within {_http.Timeout.TotalSeconds} seconds.";
            throw new TaskCanceledException(message, new TimeoutException(message, ex));
        }
    }

    private static InvalidDataException ResponseTooLarge() => new($"The Twitch OAuth response exceeds the {MaxResponseBytes}-byte limit.");

    private async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? error = null;
        // Only machine-readable error codes are retained; raw OAuth responses may contain credentials.
        try
        {
            var body = await ReadBodyAsync(response.Content, ct).ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                error = e.GetString();
            // Twitch reports some protocol states only in "message"; keep just the known machine-readable values.
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                error = KnownOAuthMessage(m.GetString()) ?? error;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { /* Keep the status for non-JSON or oversized errors. */ }
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
