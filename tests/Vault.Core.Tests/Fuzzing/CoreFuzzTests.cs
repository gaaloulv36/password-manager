using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vault.Core.Browser;
using Vault.Core.Crypto;
using Vault.Core.Generators;
using Vault.Core.Import;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Core.Storage;

namespace Vault.Core.Tests.Fuzzing;

/// <summary>
/// Fuzz targets for everything that reads data the app doesn't control: the vault file (it lives in a
/// sync folder), import files, browser messages, 2FA secrets, URLs. Each may only fail in its documented way.
/// </summary>
public class CoreFuzzTests
{
    private const string Password = "fuzz-pw";

    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    private static (UnlockedVault Vault, byte[] File) SampleVault()
    {
        var vault = UnlockedVault.Create(Password, FastKdf());
        vault.Upsert(new VaultItem { Title = "GitHub", Username = "me", Password = "p1", Urls = ["https://github.com"], TotpSecret = "JBSWY3DPEHPK3PXP" });
        var deleted = new VaultItem { Title = "Old" };
        vault.Upsert(deleted);
        vault.Delete(deleted.Id);
        vault.Upsert(new VaultItem { Title = "Mail", Password = "p2" }.WithPassword("p3", DateTimeOffset.UtcNow));
        return (vault, JsonSerializer.SerializeToUtf8Bytes(vault.Document));
    }

    [Fact]
    public void VaultFile_LoadUnlockAndMerge()
    {
        var (vault, file) = SampleVault();
        using var _ = vault;
        using var other = UnlockedVault.Create(Password, FastKdf());

        Fuzzer.Run("vault-file", [file, JsonSerializer.SerializeToUtf8Bytes(other.Document)], bytes =>
        {
            var document = VaultFileStore.Read(new MemoryStream(bytes));

            // Merging an untrusted copy must authenticate everything or refuse it.
            try
            {
                vault.Merge(document);
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
            {
            }

            // Keep Argon2 cheap in the fuzz loop; large-but-valid settings are covered by KdfParameters.Validate.
            if (document.Kdf.MemoryKiB <= 8192 && document.Kdf.Iterations <= 4)
                using (UnlockedVault.Unlock(document, Password)) { }
        },
        typeof(InvalidDataException), typeof(JsonException), typeof(InvalidMasterPasswordException), typeof(CryptographicException));

        Assert.NotEmpty(vault.GetItems()); // the open vault is never corrupted by rejected input
    }

    [Fact]
    public void Import_AllFormats() => Fuzzer.RunText("import", [
        "name,url,username,password,note\ngithub.com,https://github.com/login,me,p1,\nmail,https://mail.example/,me@x,\"p,2\",n\n",
        "\"url\",\"username\",\"password\",\"httpRealm\",\"formActionOrigin\",\"guid\",\"timeCreated\",\"timeLastUsed\",\"timePasswordChanged\"\n\"https://www.reddit.com\",\"me\",\"p1\",,\"https://www.reddit.com\",\"{g}\",\"1\",\"2\",\"3\"\n",
        "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\nWork,1,login,GitLab,,,0,\"https://a,https://b\",me,p1,JBSWY3DPEHPK3PXP\n",
        """{ "encrypted": false, "items": [ { "type": 1, "name": "GitHub", "login": { "username": "me", "password": "p1", "uris": [ { "uri": "https://github.com" } ] } }, { "type": 2, "name": "Note", "notes": "x" } ] }""",
        "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\"\n\"Root\",\"Mail\",\"me\",\"p1\",\"https://mail\",\"\",\"\"\n",
    ], text => VaultImporter.Parse(text), typeof(FormatException));

    [Fact]
    public void Csv_RoundTripsAnyFields()
    {
        var random = new Random(42);
        for (var i = 0; i < Fuzzer.Iterations; i++)
        {
            var fields = Enumerable.Range(0, 2 + random.Next(6))
                .Select(_ => Encoding.UTF8.GetString(Fuzzer.Mutate(Encoding.UTF8.GetBytes("a,b\"c\nd"), random)))
                .ToArray();
            Assert.Equal(fields, Csv.Parse(Csv.Format(fields)).Single());
        }
    }

    [Fact]
    public void Totp_ParseAndGenerate() => Fuzzer.RunText("totp", [
        "JBSWY3DPEHPK3PXP",
        "gezd gnbv gy3t qojq",
        "otpauth://totp/GitHub:me?secret=GEZDGNBVGY3TQOJQ&issuer=GitHub&algorithm=SHA256&digits=8&period=60",
    ], text => Totp.Generate(TotpParameters.Parse(text), DateTimeOffset.UtcNow),
        typeof(FormatException), typeof(UriFormatException), typeof(ArgumentException));

    [Fact]
    public void BrowserFraming() => Fuzzer.Run("framing", [
        [7, 0, 0, 0, .. "{\"a\":1}"u8],
        [2, 0, 0, 0, .. "{}"u8, 3, 0, 0, 0, .. "[1]"u8],
    ], bytes =>
    {
        using var stream = new MemoryStream(bytes);
        while (MessageFraming.ReadAsync(stream).GetAwaiter().GetResult() is not null) { }
    }, typeof(InvalidDataException));

    [Fact]
    public void BrowserEnvelope()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedMessage = BrowserProtocol.Seal(key, "pair", BrowserProtocol.ToApp, "{\"type\":\"status\"}"u8);

        Fuzzer.RunText("envelope", [sealedMessage, "AAAA", ""],
            text => BrowserProtocol.Open(key, "pair", BrowserProtocol.ToApp, text),
            typeof(CryptographicException), typeof(FormatException));
    }

    [Fact]
    public void PairingPublicKeys()
    {
        using var app = new PairingHandshake();
        using var extension = new PairingHandshake();
        Fuzzer.Run("pairing-key", [extension.PublicKey], bytes => app.Complete(bytes), typeof(CryptographicException));
    }

    /// <summary>URL matching is the phishing guard: it must never throw, whatever the page or saved URL.</summary>
    [Fact]
    public void UrlMatching_NeverThrows()
    {
        string[] seeds = ["https://github.com/login", "github.com", "https://xn--gthub-n4a.com", "http://localhost:8765/x", "https://[::1]:443/", "file:///C:/x"];
        Fuzzer.RunText("url", seeds, page =>
        {
            foreach (var saved in seeds)
            {
                UrlMatcher.Matches(saved, page);
                UrlMatcher.Matches(page, saved, exactHost: true);
            }
            UrlMatcher.Origin(page);
            UrlMatcher.DisplayHost(page);
        });
    }

    [Fact]
    public void PasswordStrength_NeverThrowsOrHangs() =>
        Fuzzer.RunText("strength", ["Password1!", "correct-horse-battery-staple", "qwerty123", "k7#Qp2!vXm9@", ""],
            text => PasswordStrength.Evaluate(text));
}
