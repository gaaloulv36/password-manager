using System.Security.Cryptography;
using Vault.Core.Crypto;
using Vault.Core.Models;
using Vault.Core.Storage;

namespace Vault.Core.Tests;

public class UnlockedVaultTests
{
    // Cheap parameters so tests stay fast. Never use these for a real vault.
    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    [Fact]
    public void SaveLoadUnlock_RoundTripsItems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pmvault");
        try
        {
            using (var vault = UnlockedVault.Create("correct horse", FastKdf()))
            {
                vault.Upsert(new VaultItem { Title = "GitHub", Username = "me", Password = "p@ss", Urls = ["https://github.com"] });
                VaultFileStore.Save(vault.Document, path);
            }

            using var reopened = UnlockedVault.Unlock(VaultFileStore.Load(path), "correct horse");
            var item = Assert.Single(reopened.GetItems());
            Assert.Equal("GitHub", item.Title);
            Assert.Equal("p@ss", item.Password);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavedFile_DoesNotContainPlaintext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pmvault");
        try
        {
            using var vault = UnlockedVault.Create("pw", FastKdf());
            vault.Upsert(new VaultItem { Title = "Bank", Password = "VerySecretValue123" });
            VaultFileStore.Save(vault.Document, path);

            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("VerySecretValue123", raw);
            Assert.DoesNotContain("Bank", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unlock_WithWrongPassword_Throws()
    {
        using var vault = UnlockedVault.Create("right", FastKdf());
        Assert.Throws<InvalidMasterPasswordException>(() => UnlockedVault.Unlock(vault.Document, "wrong"));
    }

    [Fact]
    public void ChangeMasterPassword_KeepsItemsReadable()
    {
        using var vault = UnlockedVault.Create("old", FastKdf());
        vault.Upsert(new VaultItem { Title = "Mail" });
        vault.ChangeMasterPassword("new", FastKdf());

        Assert.Throws<InvalidMasterPasswordException>(() => UnlockedVault.Unlock(vault.Document, "old"));
        using var reopened = UnlockedVault.Unlock(vault.Document, "new");
        Assert.Equal("Mail", Assert.Single(reopened.GetItems()).Title);
    }

    [Fact]
    public void Upsert_AndDelete_TrackRevisionsAndTombstones()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        var item = new VaultItem { Title = "v1" };
        vault.Upsert(item);
        vault.Upsert(item with { Title = "v2" });

        Assert.Equal(2, Assert.Single(vault.Document.Items).Revision);
        Assert.Equal("v2", Assert.Single(vault.GetItems()).Title);

        vault.Delete(item.Id);
        Assert.Empty(vault.GetItems());
        Assert.True(Assert.Single(vault.Document.Items).IsDeleted);
    }

    [Fact]
    public void SwappingItemPayloads_IsDetected()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        vault.Upsert(new VaultItem { Title = "A" });
        vault.Upsert(new VaultItem { Title = "B" });

        var items = vault.Document.Items;
        items[0] = items[0] with { Payload = items[1].Payload };

        Assert.ThrowsAny<CryptographicException>(() => vault.GetItems());
    }

    [Fact]
    public void LockedVault_RefusesAccess()
    {
        var vault = UnlockedVault.Create("pw", FastKdf());
        vault.Dispose();
        Assert.Throws<ObjectDisposedException>(() => vault.GetItems());
    }
}
