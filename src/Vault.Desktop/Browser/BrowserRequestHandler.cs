using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vault.Core.Browser;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Desktop.Services;

namespace Vault.Desktop.Browser;

/// <summary>What the browser link needs from the UI.</summary>
public interface IBrowserUi
{
    void ShowApp();

    /// <summary>Asks the user to compare the code with the one the browser shows. False if rejected or ignored.</summary>
    Task<bool> ConfirmPairingAsync(string browserName, string code);
}

/// <summary>State for one browser connection (one native-host process).</summary>
public sealed class BrowserConnection
{
    internal PendingPairing? Pending { get; set; }
}

internal sealed record PendingPairing(string BrowserName, byte[] MessageKey, Task<bool> Decision);

/// <summary>
/// Implements the extension protocol. Runs on the UI thread (the vault isn't thread-safe).
/// Plaintext messages are only used for pairing; everything else is sealed with the pairing key.
/// </summary>
public sealed class BrowserRequestHandler(VaultSession session, PairingStore pairings, IBrowserUi ui)
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, DateTimeOffset> _seenRequestIds = [];

    public async Task<JsonObject> HandleAsync(BrowserConnection connection, JsonObject message)
    {
        JsonObject reply;
        try
        {
            reply = (string?)message["type"] switch
            {
                "pair-request" => StartPairing(connection, message),
                "pair-wait" => await WaitForPairingAsync(connection),
                "encrypted" => await HandleEncryptedAsync(message),
                _ => new JsonObject { ["type"] = "error", ["error"] = "unknown-type" },
            };
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or CryptographicException)
        {
            reply = new JsonObject { ["type"] = "error", ["error"] = "bad-request" };
        }
        reply["id"] = (string?)message["id"];
        return reply;
    }

    private JsonObject StartPairing(BrowserConnection connection, JsonObject message)
    {
        var extensionKey = Convert.FromBase64String((string?)message["extPublicKey"] ?? "");
        var browser = ((string?)message["browser"])?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, 40)] : "Browser";

        using var handshake = new PairingHandshake();
        var (messageKey, code) = handshake.Complete(extensionKey);
        connection.Pending = new PendingPairing(browser, messageKey, ui.ConfirmPairingAsync(browser, code));

        return new JsonObject { ["type"] = "pair-challenge", ["appPublicKey"] = Convert.ToBase64String(handshake.PublicKey) };
    }

    private async Task<JsonObject> WaitForPairingAsync(BrowserConnection connection)
    {
        var pending = connection.Pending ?? throw new InvalidOperationException("No pairing in progress.");
        connection.Pending = null;
        try
        {
            if (!await pending.Decision) return new JsonObject { ["type"] = "pair-rejected" };
            var pairing = pairings.Add(pending.BrowserName, pending.MessageKey);
            return new JsonObject { ["type"] = "pair-approved", ["pairingId"] = pairing.Id };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pending.MessageKey);
        }
    }

    private async Task<JsonObject> HandleEncryptedAsync(JsonObject message)
    {
        var pairingId = (string?)message["pairingId"] ?? "";
        var key = pairings.GetKey(pairingId);
        if (key is null) return new JsonObject { ["type"] = "not-paired" };

        try
        {
            JsonObject request;
            try
            {
                request = JsonNode.Parse(BrowserProtocol.Open(key, pairingId, BrowserProtocol.ToApp, (string?)message["data"] ?? ""))!.AsObject();
            }
            catch (CryptographicException)
            {
                return new JsonObject { ["type"] = "not-paired" }; // e.g. the extension still has an old key
            }

            var response = IsReplay(request) ? Fail("replayed") : await HandleRequestAsync(request);
            response["id"] = (string?)request["id"];
            return new JsonObject
            {
                ["type"] = "encrypted",
                ["pairingId"] = pairingId,
                ["data"] = BrowserProtocol.Seal(key, pairingId, BrowserProtocol.ToExtension, JsonSerializer.SerializeToUtf8Bytes(response)),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Each sealed request is accepted once, and only if it's recent.</summary>
    private bool IsReplay(JsonObject request)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _seenRequestIds.Where(e => now - e.Value > MaxClockSkew * 2).Select(e => e.Key).ToList())
            _seenRequestIds.Remove(expired);

        var id = (string?)request["id"];
        var sent = DateTimeOffset.FromUnixTimeMilliseconds((long?)request["ts"] ?? 0);
        return id is null || (now - sent).Duration() > MaxClockSkew || !_seenRequestIds.TryAdd(id, now);
    }

    private Task<JsonObject> HandleRequestAsync(JsonObject request)
    {
        var type = (string?)request["type"];
        if (type == "status")
            return Task.FromResult(Ok(new JsonObject { ["locked"] = !session.IsUnlocked, ["vaultName"] = System.IO.Path.GetFileNameWithoutExtension(session.Path) }));
        if (type == "show-app")
        {
            ui.ShowApp();
            return Task.FromResult(Ok());
        }

        if (!session.IsUnlocked) return Task.FromResult(Fail("locked"));
        var url = (string?)request["url"] ?? "";

        return Task.FromResult(type switch
        {
            "get-logins" => GetLogins(url),
            "get-credential" => GetCredential(url, (string?)request["itemId"]),
            "check-login" => CheckLogin(url, (string?)request["username"], (string?)request["password"]),
            "save-login" => SaveLogin(url, (string?)request["username"], (string?)request["password"]),
            _ => Fail("unknown-type"),
        });
    }

    private List<VaultItem> MatchingItems(string url) =>
        session.Vault!.GetItems().Where(i => i.Urls.Any(saved => UrlMatcher.Matches(saved, url))).ToList();

    /// <summary>Titles and usernames only — a password is released one at a time, on a click.</summary>
    private JsonObject GetLogins(string url) => Ok(new JsonObject
    {
        ["logins"] = new JsonArray(MatchingItems(url)
            .OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => (JsonNode)new JsonObject { ["itemId"] = i.Id.ToString(), ["title"] = i.Title, ["username"] = i.Username })
            .ToArray()),
    });

    /// <summary>The page URL is checked again here, so a compromised page can't request another site's login by id.</summary>
    private JsonObject GetCredential(string url, string? itemId)
    {
        var item = MatchingItems(url).FirstOrDefault(i => i.Id.ToString() == itemId);
        if (item is null) return Fail("no-match");

        string? totp = null;
        if (item.TotpSecret is { } secret)
        {
            try { totp = Totp.Generate(TotpParameters.Parse(secret), DateTimeOffset.UtcNow); }
            catch (Exception ex) when (ex is FormatException or UriFormatException or ArgumentException) { }
        }
        return Ok(new JsonObject { ["username"] = item.Username, ["password"] = item.Password, ["totp"] = totp });
    }

    private JsonObject CheckLogin(string url, string? username, string? password)
    {
        if (string.IsNullOrEmpty(password) || UrlMatcher.Origin(url) is null) return Ok(new JsonObject { ["status"] = "ignore" });
        var existing = FindLogin(url, username);
        var status = existing is null ? "new" : existing.Password == password ? "known" : "changed";
        return Ok(new JsonObject { ["status"] = status, ["site"] = UrlMatcher.DisplayHost(url) });
    }

    private JsonObject SaveLogin(string url, string? username, string? password)
    {
        if (string.IsNullOrEmpty(password) || UrlMatcher.Origin(url) is not { } origin) return Fail("bad-request");
        username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();

        var existing = FindLogin(url, username);
        if (existing?.Password == password) return Ok(new JsonObject { ["result"] = "unchanged" });

        var item = existing is null
            ? new VaultItem { Title = UrlMatcher.DisplayHost(url) ?? origin, Username = username, Password = password, Urls = [origin] }
            : existing with { Password = password, PasswordChangedAt = DateTimeOffset.UtcNow };
        session.Vault!.Upsert(item);
        session.Save();

        session.NotifyChangedByBrowser(existing is null
            ? $"Saved new login for {item.Title} from the browser"
            : $"Updated password for {item.Title} from the browser");
        return Ok(new JsonObject { ["result"] = existing is null ? "added" : "updated" });
    }

    private VaultItem? FindLogin(string url, string? username) =>
        MatchingItems(url).FirstOrDefault(i => string.Equals(i.Username ?? "", username ?? "", StringComparison.OrdinalIgnoreCase));

    private static JsonObject Ok(JsonObject? body = null)
    {
        body ??= [];
        body["ok"] = true;
        return body;
    }

    private static JsonObject Fail(string error) => new() { ["ok"] = false, ["error"] = error };
}
