using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchDock.Core;

namespace TwitchDock.Authentication;

/// <summary>
/// Keeps one authorization's tokens in a JSON file so that a bot can restart without signing in again.
/// Pass <see cref="SaveAsync"/> as the <c>persist</c> callback of <see cref="RefreshingTokenProvider"/>: Twitch refresh tokens
/// work only once, so every rotated token must be stored before the old one is lost.
/// </summary>
/// <remarks>
/// The file is replaced atomically. On Linux and macOS it is created readable and writable only by the current user; on Windows
/// the default location inside the user profile is protected by the profile's access rules. The token is stored unencrypted:
/// anyone who can read the file can act as the authorized account. For production servers prefer an operating system or cloud
/// secret store and pass its save method as the <c>persist</c> callback instead. Use one file per authorization and process.
/// </remarks>
public sealed class FileTokenStore
{
    private const int MaxFileBytes = 64 * 1024;
    private readonly TimeProvider _time;

    /// <param name="path">The token file. Its directory is created on the first save.</param>
    /// <param name="timeProvider">The clock used to record when a token was saved; <see cref="TimeProvider.System"/> by default.</param>
    public FileTokenStore(string path, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The full path of the token file.</summary>
    public string Path { get; }

    /// <summary>
    /// Returns <c>&lt;local application data&gt;/&lt;applicationName&gt;/twitch-token.json</c>, for example
    /// <c>%LOCALAPPDATA%\MyTwitchBot\twitch-token.json</c> on Windows or <c>~/.local/share/MyTwitchBot/twitch-token.json</c> on Linux.
    /// </summary>
    public static string GetDefaultPath(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        if (applicationName is "." or ".." || applicationName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || applicationName.Contains('/') || applicationName.Contains('\\'))
            throw new ArgumentException("Use a plain folder name without path separators.", nameof(applicationName));
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("The local application data folder is not available; pass an explicit path.");
        return System.IO.Path.Combine(root, applicationName, "twitch-token.json");
    }

    /// <summary>
    /// Loads the saved token, or returns null when no file exists. <see cref="OAuthTokenResponse.ExpiresIn"/> is reduced by the
    /// time since the token was saved; an access token that expired meanwhile is refreshed on its first rejection.
    /// </summary>
    /// <exception cref="JsonException">The file is not a token file written by this class.</exception>
    public async Task<OAuthTokenResponse?> LoadAsync(CancellationToken cancellationToken = default)
    {
        byte[] content;
        try
        {
            await using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length > MaxFileBytes) throw new JsonException($"The token file exceeds {MaxFileBytes} bytes.");
            content = new byte[file.Length];
            await file.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        var stored = JsonSerializer.Deserialize(content, OAuthJsonContext.Default.StoredToken);
        if (stored?.Token is not { AccessToken.Length: > 0 } token) throw new JsonException("The token file contains no access token.");
        var elapsed = Math.Max(0, (_time.GetUtcNow() - stored.SavedAt).TotalSeconds);
        return new OAuthTokenResponse
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresIn = (int)Math.Max(0, token.ExpiresIn - elapsed),
            Scope = token.Scope,
            TokenType = token.TokenType,
            IdToken = token.IdToken,
            Kind = stored.Kind,
            ClientId = stored.ClientId,
        };
    }

    /// <summary>Saves <paramref name="token"/>, replacing the file atomically. Matches the <c>persist</c> callback of <see cref="RefreshingTokenProvider"/>.</summary>
    public async Task SaveAsync(OAuthTokenResponse token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        var stored = new StoredToken { Token = token, Kind = token.Kind, ClientId = token.ClientId, SavedAt = _time.GetUtcNow() };
        var content = JsonSerializer.SerializeToUtf8Bytes(stored, OAuthJsonContext.Default.StoredToken);
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Write next to the target and move it over, so a crash never leaves a half-written token file behind.
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using (var file = new FileStream(temporary, options))
            {
                await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Deletes the token file, for example after the user revoked the authorization. Does nothing when it does not exist.</summary>
    public void Delete()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }

    public override string ToString() => $"FileTokenStore [{Path}]";
}

/// <summary>The token file format: the token response plus the metadata that is not part of Twitch's JSON.</summary>
internal sealed class StoredToken
{
    public int Version { get; init; } = 1;
    public OAuthTokenResponse? Token { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<TwitchTokenKind>))]
    public TwitchTokenKind Kind { get; init; }
    public string? ClientId { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}
