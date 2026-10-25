using System.Net;
using System.Security.Cryptography;
using System.Text;
using VaultForge.Core.Generators;
using VaultForge.Core.Models;
using VaultForge.Core.Security;

namespace VaultForge.Core.Tests;

public class PwnedPasswordsClientTests
{
    /// <summary>Fake HIBP: records what was requested and answers from a fixed range body.</summary>
    private sealed class FakeHibp(string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string?> PaddingHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            PaddingHeaders.Add(request.Headers.TryGetValues("Add-Padding", out var v) ? v.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static string Sha1(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    [Fact]
    public async Task SendsOnlyTheFirstFiveHashCharacters_AndFindsTheCount()
    {
        var hash = Sha1("password");
        var hibp = new FakeHibp($"0018A45C4D1DEF81644B54AB7F969B88D65:3\r\n{hash[5..]}:9545824\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:0\r\n");
        var client = new PwnedPasswordsClient(new HttpClient(hibp));

        var count = await client.GetBreachCountAsync("password");

        Assert.Equal(9545824, count);
        var request = Assert.Single(hibp.Requests);
        Assert.Equal($"https://api.pwnedpasswords.com/range/{hash[..5]}", request.ToString());
        Assert.DoesNotContain(hash[5..], request.ToString());
        Assert.Equal("true", Assert.Single(hibp.PaddingHeaders));
    }

    [Fact]
    public async Task NotInRange_IsZero_AndPaddingEntriesDontCount()
    {
        var hash = Sha1("a-unique-password-nobody-uses");
        var client = new PwnedPasswordsClient(new HttpClient(new FakeHibp($"{hash[5..]}:0\r\n")));

        Assert.Equal(0, await client.GetBreachCountAsync("a-unique-password-nobody-uses"));
    }

    [Fact]
    public async Task SameRange_IsFetchedOnce()
    {
        var hibp = new FakeHibp("");
        var client = new PwnedPasswordsClient(new HttpClient(hibp));

        await client.GetBreachCountAsync("password");
        await client.GetBreachCountAsync("password");

        Assert.Single(hibp.Requests);
    }
}

public class PasswordHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FindsWeakReusedAndOldPasswords()
    {
        VaultItem[] items =
        [
            new() { Title = "Strong", Password = "k7#Qp2!vXm9@Lr4$", CreatedAt = Now },
            new() { Title = "Weak", Password = "Password1!", CreatedAt = Now },
            new() { Title = "Shop A", Password = "same-Long-pass-77!", CreatedAt = Now },
            new() { Title = "Shop B", Password = "same-Long-pass-77!", CreatedAt = Now },
            new() { Title = "Old", Password = "x9$Kq!2vLm#8Pz@w", CreatedAt = Now.AddYears(-3), PasswordChangedAt = Now.AddYears(-2) },
            new() { Title = "Recently changed", Password = "c4!Rt$8kQz@2Vm#x", CreatedAt = Now.AddYears(-5), PasswordChangedAt = Now.AddDays(-10) },
            new() { Title = "No password (note)", Notes = "wifi" },
        ];

        var report = PasswordHealth.Analyze(items, Now);
        ItemHealth Get(string title) => report.Items.Single(h => h.Item.Title == title);

        Assert.Equal(6, report.Total); // the note without a password isn't scored
        Assert.False(Get("Strong").HasIssues);
        Assert.True(Get("Weak").IsWeak);
        Assert.Equal(1, Get("Shop A").ReusedCount);
        Assert.True(Get("Shop B").IsReused);
        Assert.True(Get("Old").IsOld);
        Assert.False(Get("Recently changed").IsOld);
        Assert.Null(Get("Strong").BreachCount); // no breach check run yet
        Assert.Equal(33, report.Score);          // 2 of 6 have no issues
    }

    [Fact]
    public void BreachedComeFirst()
    {
        var breached = new VaultItem { Title = "Z breached", Password = "k7#Qp2!vXm9@Lr4$", CreatedAt = Now };
        var weak = new VaultItem { Title = "A weak", Password = "dragon", CreatedAt = Now };

        var report = PasswordHealth.Analyze([weak, breached], Now, new Dictionary<Guid, int> { [breached.Id] = 42 });

        Assert.Equal("Z breached", report.Items[0].Item.Title);
        Assert.Equal(42, report.Items[0].BreachCount);
        Assert.Equal(0, report.Items[1].BreachCount);
    }

    [Fact]
    public void EmptyVault_IsPerfect() => Assert.Equal(100, PasswordHealth.Analyze([], Now).Score);
}
