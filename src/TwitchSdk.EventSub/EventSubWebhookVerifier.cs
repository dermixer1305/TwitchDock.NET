using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwitchSdk.EventSub;

public sealed class EventSubWebhookVerifier
{
    private readonly byte[] _secret;
    private readonly TimeProvider _time;
    public EventSubWebhookVerifier(string secret, TimeProvider? timeProvider = null)
    {
        if (secret is not { Length: >= 10 and <= 100 } || secret.Any(c => c > 127)) throw new ArgumentException("Secret must contain 10 to 100 ASCII characters.", nameof(secret));
        _secret = Encoding.ASCII.GetBytes(secret);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Authenticates raw bytes before parsing. The host must persist/deduplicate by message ID before acknowledging.</summary>
    public EventSubPayload VerifyAndParse(string messageId, string timestamp, string signature, ReadOnlySpan<byte> rawBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (rawBody.Length > 1024 * 1024) throw new ArgumentException("EventSub body exceeds the 1 MiB limit.", nameof(rawBody));
        if (!TryTimestamp(timestamp, out var sentAt) || sentAt < _time.GetUtcNow() - TimeSpan.FromMinutes(10) || sentAt > _time.GetUtcNow() + TimeSpan.FromMinutes(1))
            throw new CryptographicException("Invalid or stale EventSub timestamp.");
        if (signature is null || !signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71)
            throw new CryptographicException("Invalid EventSub signature format.");
        byte[] received;
        try { received = Convert.FromHexString(signature.AsSpan(7)); }
        catch (FormatException) { throw new CryptographicException("Invalid EventSub signature format."); }
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _secret);
        hmac.AppendData(Encoding.UTF8.GetBytes(messageId));
        hmac.AppendData(Encoding.UTF8.GetBytes(timestamp));
        hmac.AppendData(rawBody);
        if (!CryptographicOperations.FixedTimeEquals(received, hmac.GetHashAndReset())) throw new CryptographicException("EventSub signature mismatch.");
        return JsonSerializer.Deserialize(rawBody, EventSubJsonContext.Default.EventSubPayload) ?? throw new JsonException("Empty EventSub payload.");
    }

    internal static bool TryTimestamp(string? value, out DateTimeOffset timestamp)
    {
        // DateTimeOffset stores seven fractional digits; Twitch sends up to nine. Only normalize for age checks.
        var normalized = value;
        if (value is not null && value.EndsWith('Z'))
        {
            var dot = value.IndexOf('.');
            if (dot >= 0 && value.Length - dot - 2 is > 7 and <= 9 && value.AsSpan(dot + 1, value.Length - dot - 2).IndexOfAnyExceptInRange('0', '9') < 0)
                normalized = value[..(dot + 8)] + "Z";
        }
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }
}
