using System.Security.Cryptography;
using VaultForge.Core.Browser;
using VaultForge.Core.Crypto;
using VaultForge.Core.Models;

namespace VaultForge.Core.Tests;

public class PasswordHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ChangingThePassword_KeepsTheOldOne()
    {
        var item = new VaultItem { Title = "Mail", Password = "first" };

        var changed = item.WithPassword("second", T0);

        Assert.Equal("second", changed.Password);
        Assert.Equal(T0, changed.PasswordChangedAt);
        Assert.Equal([new PasswordHistoryEntry("first", T0)], changed.PasswordHistory);
    }

    [Fact]
    public void SamePassword_ChangesNothing()
    {
        var item = new VaultItem { Title = "Mail", Password = "same" };
        Assert.Same(item, item.WithPassword("same", T0));
    }

    [Fact]
    public void HistoryIsNewestFirst_CappedAndWithoutTheCurrentPassword()
    {
        var item = new VaultItem { Title = "Mail", Password = "p0" };
        for (var i = 1; i <= 15; i++) item = item.WithPassword($"p{i}", T0.AddDays(i));

        Assert.Equal(VaultItem.MaxPasswordHistory, item.PasswordHistory.Count);
        Assert.Equal("p14", item.PasswordHistory[0].Password);
        Assert.Equal("p5", item.PasswordHistory[^1].Password);

        // Going back to an old password removes it from the history (it's current again).
        var reverted = item.WithPassword("p10", T0.AddDays(20));
        Assert.DoesNotContain(reverted.PasswordHistory, h => h.Password == "p10");
        Assert.Equal("p15", reverted.PasswordHistory[0].Password);
    }

    [Fact]
    public void FirstPassword_HasNoHistory()
    {
        var item = new VaultItem { Title = "Mail" }.WithPassword("first", T0);
        Assert.Empty(item.PasswordHistory);
    }

    [Fact]
    public void History_SurvivesSavingAndUnlocking()
    {
        using var vault = UnlockedVault.Create("pw", new KdfParameters(RandomNumberGenerator.GetBytes(16), 1024, 1, 1));
        vault.Upsert(new VaultItem { Title = "Mail", Password = "old" }.WithPassword("new", T0));

        using var reopened = UnlockedVault.Unlock(vault.Document, "pw");

        Assert.Equal("old", Assert.Single(Assert.Single(reopened.GetItems()).PasswordHistory).Password);
    }
}

public class ExactHostMatchingTests
{
    [Theory]
    [InlineData("https://google.com", "https://google.com/", true)]
    [InlineData("https://google.com", "https://www.google.com/", true)]       // www. is still the same site
    [InlineData("https://google.com", "https://sites.google.com/view/x", false)]
    [InlineData("https://google.com", "https://accounts.google.com/", false)]
    public void ExactHost_RejectsSubdomains(string saved, string page, bool expected) =>
        Assert.Equal(expected, UrlMatcher.Matches(saved, page, exactHost: true));
}
