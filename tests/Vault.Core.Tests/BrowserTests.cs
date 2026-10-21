using System.Security.Cryptography;
using System.Text;
using Vault.Core.Browser;

namespace Vault.Core.Tests;

public class UrlMatcherTests
{
    [Theory]
    // Same site / subdomains
    [InlineData("https://github.com", "https://github.com/login", true)]
    [InlineData("https://github.com/login", "https://www.github.com/session", true)]
    [InlineData("github.com", "https://github.com/", true)]
    [InlineData("https://google.com", "https://accounts.google.com/signin", true)]
    [InlineData("http://localhost:8765", "http://localhost:8765/login", true)]
    // Phishing look-alikes
    [InlineData("https://github.com", "https://g1thub.com/login", false)]
    [InlineData("https://github.com", "https://github.com.evil.example/login", false)]
    [InlineData("https://github.com", "https://evilgithub.com/", false)]
    [InlineData("https://github.com", "https://gіthub.com/", false)] // Cyrillic "і"
    // Subdomain logins don't leak to the parent or siblings
    [InlineData("https://accounts.google.com", "https://google.com/", false)]
    [InlineData("https://accounts.google.com", "https://evil.google.com/", false)]
    // No HTTPS → HTTP downgrade, ports must match, only web pages
    [InlineData("https://bank.example", "http://bank.example/", false)]
    [InlineData("http://localhost:8765", "http://localhost:9999/", false)]
    [InlineData("https://github.com", "file:///C:/github.com/login.html", false)]
    // A saved bare TLD must not match everything
    [InlineData("https://com", "https://anything.com/", false)]
    public void Matches(string saved, string page, bool expected) => Assert.Equal(expected, UrlMatcher.Matches(saved, page));

    [Fact]
    public void OriginAndDisplayHost()
    {
        Assert.Equal("https://www.netflix.com", UrlMatcher.Origin("https://www.netflix.com/login?next=/browse"));
        Assert.Equal("netflix.com", UrlMatcher.DisplayHost("https://www.netflix.com/login"));
    }
}

public class BrowserProtocolTests
{
    private static byte[] FromB64Url(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/') + new string('=', (4 - s.Length % 4) % 4));

    private static ECDiffieHellman Key(string d, string x, string y) => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = FromB64Url(d),
        Q = new ECPoint { X = FromB64Url(x), Y = FromB64Url(y) },
    });

    // Fixed vector shared with extension/test/protocol.test.mjs — both sides must derive the same key and code.
    [Fact]
    public void Pairing_MatchesSharedTestVector()
    {
        using var app = new PairingHandshake(Key("_9vC1sAFSOYHYKWz0bcOhlaDNcF62nG45x0rHzU_dFw",
            "rnYem5NxD-NbjRjmVbQtVWig40flkPZIYSQB2kVcRm0", "aF9HaXA8pnY1pIi0NK2VdttYq3egmGC0VxuzGt7fL_k"));
        byte[] extensionPublicKey = [0x04, .. FromB64Url("3tH7l6zxkaiu1n0cj1zK1RACpBBBZe-6JBTuog9zEJ8"), .. FromB64Url("jVE5vLBogLbBTDgOs5Y65TnstIto2fMht47vtllU68k")];

        var (key, code) = app.Complete(extensionPublicKey);

        Assert.Equal("6ecd97cf96f84ccde7f3d1d36b596b5a7e85853a78e70b5be499dddc442d55d8", Convert.ToHexStringLower(key));
        Assert.Equal("816 954", code);
    }

    [Fact]
    public void Pairing_RejectsInvalidPoint()
    {
        using var app = new PairingHandshake();
        var notOnCurve = new byte[65];
        notOnCurve[0] = 0x04;
        notOnCurve[64] = 1;

        Assert.ThrowsAny<CryptographicException>(() => app.Complete(notOnCurve));
        Assert.ThrowsAny<CryptographicException>(() => app.Complete(new byte[33]));
    }

    [Fact]
    public void Envelope_RoundTripsAndBindsDirectionAndPairing()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedMessage = BrowserProtocol.Seal(key, "pair-1", BrowserProtocol.ToApp, "{\"type\":\"status\"}"u8);

        Assert.Equal("{\"type\":\"status\"}", Encoding.UTF8.GetString(BrowserProtocol.Open(key, "pair-1", BrowserProtocol.ToApp, sealedMessage)));
        Assert.ThrowsAny<CryptographicException>(() => BrowserProtocol.Open(key, "pair-1", BrowserProtocol.ToExtension, sealedMessage));
        Assert.ThrowsAny<CryptographicException>(() => BrowserProtocol.Open(key, "pair-2", BrowserProtocol.ToApp, sealedMessage));
    }

    [Fact]
    public async Task Framing_RoundTripsAndRejectsOversizedFrames()
    {
        using var stream = new MemoryStream();
        await MessageFraming.WriteAsync(stream, "{\"a\":1}"u8.ToArray());
        await MessageFraming.WriteAsync(stream, "{}"u8.ToArray());
        stream.Position = 0;

        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString((await MessageFraming.ReadAsync(stream))!));
        Assert.Equal("{}", Encoding.UTF8.GetString((await MessageFraming.ReadAsync(stream))!));
        Assert.Null(await MessageFraming.ReadAsync(stream));

        using var huge = new MemoryStream(BitConverter.GetBytes(MessageFraming.MaxMessageSize + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => MessageFraming.ReadAsync(huge));
    }
}
