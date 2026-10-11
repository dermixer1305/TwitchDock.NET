using System.Text.Json;
using TwitchDock.Authentication;
using TwitchDock.Core;

namespace TwitchDock.Tests;

public sealed class FileTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "twitchdock-tokens-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Obtains the token through a simulated refresh, so it carries the kind and client ID the SDK sets.</summary>
    private static async Task<OAuthTokenResponse> UserToken(string access, string refresh, int expiresIn = 14400)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json(
            $"{{\"access_token\":\"{access}\",\"refresh_token\":\"{refresh}\",\"expires_in\":{expiresIn},\"scope\":[\"user:read:chat\",\"user:write:chat\"],\"token_type\":\"bearer\"}}"))));
        return await new TwitchOAuthClient(http).RefreshAsync("client", "previous");
    }

    [Fact]
    public async Task MissingFileLoadsAsNull()
    {
        var store = new FileTokenStore(Path.Combine(_directory, "missing", "token.json"));
        Assert.Null(await store.LoadAsync());
        store.Delete(); // no file and no directory: nothing to do
    }

    [Fact]
    public async Task SaveAndLoadRoundTripTokenAndMetadataAndReduceRemainingLifetime()
    {
        var time = new ManualTimeProvider();
        var store = new FileTokenStore(Path.Combine(_directory, "nested", "token.json"), time);
        await store.SaveAsync(await UserToken("access-1", "refresh-1"));
        time.Advance(TimeSpan.FromMinutes(10));

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(("access-1", "refresh-1"), (loaded.AccessToken, loaded.RefreshToken));
        Assert.Equal(["user:read:chat", "user:write:chat"], loaded.Scope);
        Assert.Equal((TwitchTokenKind.User, "client"), (loaded.Kind, loaded.ClientId));
        Assert.Equal(14400 - 600, loaded.ExpiresIn);

        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await store.LoadAsync())!.ExpiresIn);
    }

    [Fact]
    public async Task SaveReplacesTheFileWithoutLeavingTemporaryFiles()
    {
        var path = Path.Combine(_directory, "token.json");
        var store = new FileTokenStore(path);
        await store.SaveAsync(await UserToken("access-1", "refresh-1"));
        await store.SaveAsync(await UserToken("access-2", "refresh-2"));
        Assert.Equal("refresh-2", (await store.LoadAsync())!.RefreshToken);
        Assert.Equal([path], Directory.GetFiles(_directory));
        Assert.DoesNotContain("access-1", await File.ReadAllTextAsync(path));

        store.Delete();
        Assert.False(File.Exists(path));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task FileIsReadableOnlyByTheCurrentUserOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_directory, "private", "token.json");
        await new FileTokenStore(path).SaveAsync(await UserToken("access", "refresh"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task InvalidOrOversizedFilesAreRejected()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "token.json");
        var store = new FileTokenStore(path);
        await File.WriteAllTextAsync(path, "not json");
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync());
        await File.WriteAllTextAsync(path, "{\"version\":1}");
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        await File.WriteAllTextAsync(path, new string(' ', 70_000));
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task RotatedRefreshTokensArePersistedThroughTheProvider()
    {
        var store = new FileTokenStore(Path.Combine(_directory, "token.json"));
        var initial = await UserToken("access-1", "refresh-1");
        await store.SaveAsync(initial);
        string? usedRefreshToken = null;
        var rotated = await UserToken("access-2", "refresh-2");
        using var provider = new RefreshingTokenProvider((refresh, _) =>
        {
            usedRefreshToken = refresh;
            return Task.FromResult(rotated);
        }, await store.LoadAsync(), store.SaveAsync);

        var refreshed = await provider.RefreshTokenAsync(await provider.GetTokenAsync());
        Assert.Equal("refresh-1", usedRefreshToken);
        Assert.Equal("access-2", refreshed.Value);
        Assert.Equal("refresh-2", (await store.LoadAsync())!.RefreshToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void DefaultPathRejectsNamesThatAreNotPlainFolders(string name)
        => Assert.ThrowsAny<ArgumentException>(() => FileTokenStore.GetDefaultPath(name));

    [Fact]
    public void DefaultPathIsInsideLocalApplicationData()
    {
        var path = FileTokenStore.GetDefaultPath("MyTwitchBot");
        Assert.Equal("twitch-token.json", Path.GetFileName(path));
        Assert.Equal("MyTwitchBot", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path, StringComparison.Ordinal);
        Assert.DoesNotContain("redacted", new FileTokenStore(path).ToString(), StringComparison.Ordinal);
    }
}
