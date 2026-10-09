using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TwitchSdk.Core;

/// <summary>Shared transport. Does not own HttpClient; use IHttpClientFactory or manage its lifetime externally.</summary>
public sealed class TwitchHttpClient
{
    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private readonly TwitchHttpOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TwitchHttpClient> _logger;
    private long _blockedUntilTicks;

    public TwitchHttpClient(HttpClient httpClient, IAccessTokenProvider tokens, TwitchHttpOptions options,
        TimeProvider? timeProvider = null, ILogger<TwitchHttpClient>? logger = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<TwitchHttpClient>.Instance;
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> responseType,
        IEnumerable<KeyValuePair<string, string?>>? query = null, ReadOnlyMemory<byte>? jsonBody = null,
        TwitchAuthorizationRequirement? authorization = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, query, jsonBody, authorization, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, responseType, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Twitch returned an empty JSON response.");
    }

    public async Task SendAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string?>>? query = null, ReadOnlyMemory<byte>? jsonBody = null,
        TwitchAuthorizationRequirement? authorization = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, query, jsonBody, authorization, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a text response through the shared retry/error pipeline. Public endpoints can bypass token acquisition.</summary>
    public async Task<string> SendTextAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string?>>? query = null, bool authenticated = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, query, null, null, cancellationToken, authenticated).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string?>>? query, ReadOnlyMemory<byte>? jsonBody, TwitchAuthorizationRequirement? authorization, CancellationToken ct, bool authenticated = true)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Callers supply a Helix path, never a URI. This prevents sending bearer tokens to another origin.
        if (path.StartsWith('/') || path.Contains(':') || path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Contains('%') || path.Split('/').Any(p => p is "." or ".."))
            throw new ArgumentException("Expected a relative Helix endpoint path.", nameof(path));
        var encoded = query?.Where(p => p.Value is not null).Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
        var suffix = encoded is null ? "" : string.Join("&", encoded);
        var uri = new Uri(_options.BaseAddress, path + (suffix.Length == 0 ? "" : "?" + suffix));
        var token = authenticated ? await _tokens.GetTokenAsync(ct).ConfigureAwait(false) : null;
        int rateRetries = 0, transientRetries = 0;
        bool refreshed = false;
        // Snapshot mutable caller memory before retries.
        var body = jsonBody?.ToArray();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (token?.ClientId is not null && token.ClientId != _options.ClientId)
                throw new TwitchAuthorizationException("The token belongs to a different client ID.");
            if (token is not null) authorization?.Validate(token);
            while (true)
            {
                var delay = new DateTimeOffset(Interlocked.Read(ref _blockedUntilTicks), TimeSpan.Zero) - _time.GetUtcNow();
                if (delay <= TimeSpan.Zero) break;
                await Task.Delay(delay, _time, ct).ConfigureAwait(false);
                // Another in-flight response may have extended the shared reset while this request waited.
            }
            using var request = new HttpRequestMessage(method, uri);
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
                request.Headers.Add("Client-Id", _options.ClientId);
            }
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var retryDelay = GetRetryDelay(response);
            if (response.Headers.TryGetValues("Ratelimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
                BlockFor(retryDelay);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed && token is not null)
            {
                AccessToken next;
                try { next = await _tokens.RefreshTokenAsync(token, ct).ConfigureAwait(false); }
                catch { response.Dispose(); throw; }
                refreshed = true;
                if (next.Value != token.Value) { response.Dispose(); token = next; continue; }
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests && rateRetries++ < _options.MaxRateLimitRetries && retryDelay <= _options.MaxRetryDelay)
            {
                response.Dispose();
                BlockFor(retryDelay);
                _logger.LogDebug("Twitch rate limit reached; retrying after {Delay}.", retryDelay);
                continue;
            }
            if ((method == HttpMethod.Get || method == HttpMethod.Head) && (int)response.StatusCode is 500 or 502 or 503 or 504 && transientRetries++ < _options.MaxTransientRetries && retryDelay <= _options.MaxRetryDelay)
            {
                response.Dispose();
                await Task.Delay(retryDelay, _time, ct).ConfigureAwait(false);
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                string? error = null;
                string? existingSubscriptionId = null;
                var message = $"Twitch API returned HTTP {(int)response.StatusCode}.";
                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (document.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString();
                        if (document.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString() ?? message;
                        if (response.StatusCode == HttpStatusCode.Conflict && path == "eventsub/subscriptions"
                            && document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                            existingSubscriptionId = id.GetString();
                    }
                }
                catch (JsonException) { /* Preserve status for non-JSON upstream errors. */ }
                var requestId = response.Headers.TryGetValues("Twitch-Trace-Id", out var ids) ? ids.FirstOrDefault() : null;
                throw new TwitchApiException(response.StatusCode, error, message, requestId, existingSubscriptionId);
            }
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (response.Headers.RetryAfter?.Date is { } date) delay = date - _time.GetUtcNow();
        if (response.Headers.TryGetValues("Ratelimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and <= 253402300799)
        {
            var resetDelay = DateTimeOffset.FromUnixTimeSeconds(seconds) - _time.GetUtcNow();
            if (!delay.HasValue || resetDelay > delay) delay = resetDelay;
        }
        return delay is { } value && value > TimeSpan.Zero ? value : _options.FallbackRetryDelay;
    }

    private void BlockFor(TimeSpan delay)
    {
        if (delay > _options.MaxRetryDelay) return;
        var until = (_time.GetUtcNow() + delay).UtcTicks;
        long observed;
        do { observed = Interlocked.Read(ref _blockedUntilTicks); if (observed >= until) return; }
        while (Interlocked.CompareExchange(ref _blockedUntilTicks, until, observed) != observed);
    }
}
