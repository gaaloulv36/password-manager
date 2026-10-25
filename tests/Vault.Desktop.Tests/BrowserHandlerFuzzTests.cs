using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vault.Core.Browser;
using Vault.Core.Models;
using Vault.Desktop.Browser;
using Vault.Desktop.Services;

namespace Vault.Desktop.Tests;

/// <summary>
/// Fuzzes the browser request handler — everything a browser (or anything pretending to be one) can send.
/// It must always answer, and must never release a password for a site the entry doesn't belong to.
/// </summary>
public sealed class BrowserHandlerFuzzTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-handler-fuzz-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeUi : IBrowserUi
    {
        public void ShowApp() { }
        public Task<bool> ConfirmPairingAsync(string browserName, string code) => Task.FromResult(false); // never approve
    }

    private static readonly string[] Types = ["status", "show-app", "get-logins", "get-credential", "check-login", "save-login", "nope", ""];
    private static readonly string[] Urls =
    [
        "https://github.com/login", "https://g1thub.com/login", "https://github.com.evil.example/", "http://github.com/",
        "https://gist.github.com/", "https://xn--gthub-n4a.com/", "javascript:alert(1)", "", "not a url", "https://[::1]/",
    ];

    /// <summary>A random JSON value of any shape, so every field gets the wrong type sometimes.</summary>
    private static JsonNode? RandomValue(Random random, int depth = 0) => random.Next(depth > 2 ? 5 : 8) switch
    {
        0 => null,
        1 => JsonValue.Create(random.Next(2) == 0),
        2 => JsonValue.Create(random.NextInt64(long.MinValue, long.MaxValue)),
        3 => JsonValue.Create(random.NextDouble() * 1e300),
        4 => JsonValue.Create(Urls[random.Next(Urls.Length)]),
        5 => new JsonArray(RandomValue(random, depth + 1), RandomValue(random, depth + 1)),
        6 => new JsonObject { ["x"] = RandomValue(random, depth + 1) },
        _ => JsonValue.Create(new string((char)random.Next(32, 0xD7FF), random.Next(200))),
    };

    /// <summary>Usually the good value, sometimes garbage: requests stay valid enough to reach deep code paths.</summary>
    private static JsonNode? Maybe(Random random, JsonNode? good, int oneIn = 4) => random.Next(oneIn) == 0 ? RandomValue(random) : good;

    [Fact]
    public void Handler_AlwaysAnswers_AndNeverLeaksPasswordsToOtherSites()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunFuzz(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private void RunFuzz()
    {
        Directory.CreateDirectory(_dir);
        var session = new VaultSession();
        session.Create(Path.Combine(_dir, "Fuzz.pmvault"), "fuzz master password");
        var github = new VaultItem { Title = "GitHub", Username = "me", Password = "gh-secret", Urls = ["https://github.com"] };
        var bank = new VaultItem { Title = "Bank", Username = "me", Password = "bank-secret", Urls = ["https://bank.example"], MatchExactHost = true };
        session.Vault!.Upsert(github);
        session.Vault.Upsert(bank);

        var pairings = new PairingStore(Path.Combine(_dir, "pairings.json"));
        var key = RandomNumberGenerator.GetBytes(32);
        var pairing = pairings.Add("Fuzz browser", key);
        var handler = new BrowserRequestHandler(session, pairings, new FakeUi());
        var random = new Random(1234);
        string[] itemIds = [github.Id.ToString(), bank.Id.ToString(), Guid.NewGuid().ToString(), ""];

        int released = 0, refused = 0;
        for (var i = 0; i < 5000; i++)
        {
            var connection = new BrowserConnection();
            JsonObject message;
            JsonObject? inner = null;

            if (random.Next(3) == 0)
            {
                // Plaintext: pairing messages and garbage.
                message = new JsonObject
                {
                    ["type"] = Maybe(random, new[] { "pair-request", "pair-wait", "encrypted", "x" }[random.Next(4)]),
                    ["id"] = Maybe(random, "id-" + i),
                    ["extPublicKey"] = Maybe(random, Convert.ToBase64String(RandomNumberGenerator.GetBytes(random.Next(2) == 0 ? 65 : random.Next(100)))),
                    ["browser"] = RandomValue(random),
                    ["pairingId"] = Maybe(random, pairing.Id),
                    ["data"] = RandomValue(random),
                };
            }
            else
            {
                // Properly sealed requests with mangled contents: what a buggy or malicious extension could send.
                inner = new JsonObject
                {
                    ["type"] = Maybe(random, random.Next(2) == 0 ? "get-credential" : Types[random.Next(Types.Length)], 10),
                    ["id"] = Maybe(random, Guid.NewGuid().ToString(), 10),
                    ["ts"] = Maybe(random, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10),
                    ["url"] = Maybe(random, Urls[random.Next(Urls.Length)], 10),
                    ["itemId"] = Maybe(random, itemIds[random.Next(itemIds.Length)], 10),
                    ["username"] = Maybe(random, "me"),
                    ["password"] = Maybe(random, "new-" + random.Next(5)),
                };
                message = new JsonObject
                {
                    ["type"] = "encrypted",
                    ["id"] = "id-" + i,
                    ["pairingId"] = pairing.Id,
                    ["data"] = BrowserProtocol.Seal(key, pairing.Id, BrowserProtocol.ToApp, JsonSerializer.SerializeToUtf8Bytes(inner)),
                };
            }

            JsonObject reply;
            try
            {
                reply = handler.HandleAsync(connection, message).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"Iteration {i}: handler threw {ex.GetType().Name} for {message.ToJsonString()}\n{ex}");
            }
            Assert.NotNull(reply);

            if (inner is null || (string?)reply["type"] != "encrypted") continue;
            var response = JsonNode.Parse(BrowserProtocol.Open(key, pairing.Id, BrowserProtocol.ToExtension, (string)reply["data"]!))!.AsObject();
            if ((string?)response["error"] == "no-match") refused++;
            if (response["password"] is not JsonValue releasedPassword) continue;
            released++;

            // A password may only go to a page that matches the entry it belongs to.
            var url = inner["url"] is JsonValue u && u.TryGetValue<string>(out var s) ? s : "";
            var owner = (string?)releasedPassword == "gh-secret" ? github : (string?)releasedPassword == "bank-secret" ? bank
                : session.Vault.GetItems().First(it => it.Password == (string?)releasedPassword);
            Assert.True(owner.Urls.Any(saved => UrlMatcher.Matches(saved, url, owner.MatchExactHost)),
                $"Iteration {i}: released {owner.Title}'s password to '{url}'");
        }
        // Make sure the fuzzer actually reached both outcomes, not just error paths.
        Assert.True(released > 0, "no password was ever released: the fuzzer never reached that code");
        Assert.True(refused > 0, "no request was ever refused for a wrong site");
        Console.WriteLine($"handler fuzz: {released} releases checked, {refused} wrong-site refusals");
        session.Lock();
    }
}
