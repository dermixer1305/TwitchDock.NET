using System.Net;
using System.Security.Cryptography;
using System.Text;
using TwitchDock.Authentication;

namespace TwitchDock.Tests;

/// <summary>Signs ID tokens and serves the matching JWKS. Each instance owns its key cache, so tests never share key state.</summary>
internal sealed class OpenIdFixture : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private int _fetches;
    private volatile bool _failFetches;
    private volatile bool _emptyKeys;
    private volatile Task? _fetchGate;

    public OpenIdFixture()
    {
        Cache = new OpenIdSigningKeyCache(Time);
        Http = new HttpClient(CreateHandler());
    }

    public ManualTimeProvider Time { get; } = new();
    public OpenIdSigningKeyCache Cache { get; }
    public HttpClient Http { get; }
    public int Fetches => Volatile.Read(ref _fetches);
    /// <summary>Makes the keys endpoint answer HTTP 500.</summary>
    public bool FailFetches { get => _failFetches; set => _failFetches = value; }
    /// <summary>Makes the keys endpoint publish an empty key set.</summary>
    public bool EmptyKeys { get => _emptyKeys; set => _emptyKeys = value; }
    /// <summary>Holds key responses until the task completes or the request is cancelled.</summary>
    public Task? FetchGate { get => _fetchGate; set => _fetchGate = value; }

    public TwitchOAuthClient Client() => new(Http, Time, Cache);

    public HttpMessageHandler CreateHandler() => new TestHttpHandler(async (request, ct) =>
    {
        Assert.Equal("https://id.twitch.tv/oauth2/keys", request.RequestUri!.AbsoluteUri);
        Interlocked.Increment(ref _fetches);
        if (FetchGate is { } gate) await gate.WaitAsync(ct);
        if (FailFetches) return TestHttpHandler.Json("{}", HttpStatusCode.InternalServerError);
        if (EmptyKeys) return TestHttpHandler.Json("""{"keys":[]}""");
        var key = _rsa.ExportParameters(false);
        return TestHttpHandler.Json($$"""{"keys":[{"alg":"RS256","e":"{{B64(key.Exponent!)}}","kid":"1","kty":"RSA","n":"{{B64(key.Modulus!)}}","use":"sig"}]}""");
    });

    /// <summary>Claims issued now for audience client, valid for 15 minutes. Extra claims start with a comma.</summary>
    public string Payload(string? nonce = "n1", string audience = "\"client\"", string claims = "")
    {
        var now = Time.GetUtcNow().ToUnixTimeSeconds();
        var nonceClaim = nonce is null ? "" : $",\"nonce\":\"{nonce}\"";
        return $$"""{"iss":"https://id.twitch.tv/oauth2","sub":"713936733","aud":{{audience}},"exp":{{now + 900}},"iat":{{now}}{{nonceClaim}}{{claims}}}""";
    }

    public string Token(string? payload = null, string alg = "RS256", string kid = "1")
        => Sign($$"""{"alg":"{{alg}}","kid":"{{kid}}","typ":"JWT"}""", payload ?? Payload());

    public static string B64(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Sign(string header, string payload)
    {
        var signingInput = B64(Encoding.UTF8.GetBytes(header)) + "." + B64(Encoding.UTF8.GetBytes(payload));
        return signingInput + "." + B64(_rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    public void Dispose()
    {
        Http.Dispose();
        _rsa.Dispose();
    }
}
