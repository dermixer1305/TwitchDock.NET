namespace TwitchDock.Helix.Extensions;

/// <summary>
/// A decoded extension shared secret used to sign Extension Backend Service (EBS) JWTs with HS256.
/// The key bytes are never exposed, serialized or included in <see cref="ToString"/>.
/// </summary>
public sealed class ExtensionSecret
{
    private readonly byte[] _key;

    private ExtensionSecret(byte[] key) => _key = key;

    /// <summary>Decodes the base64 secret shown in the developer console or returned as <c>content</c> by Get Extension Secrets.</summary>
    /// <exception cref="ArgumentException">The value is empty, not valid base64, or decodes to no bytes.</exception>
    public static ExtensionSecret FromBase64(string base64Secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Secret);
        byte[] key;
        try { key = Convert.FromBase64String(base64Secret); }
        // The secret value is deliberately not echoed in the message.
        catch (FormatException) { throw new ArgumentException("The extension secret must be a base64-encoded string.", nameof(base64Secret)); }
        if (key.Length == 0) throw new ArgumentException("The extension secret must not be empty.", nameof(base64Secret));
        return new ExtensionSecret(key);
    }

    internal ReadOnlySpan<byte> Key => _key;

    public override string ToString() => "ExtensionSecret [redacted]";
}
