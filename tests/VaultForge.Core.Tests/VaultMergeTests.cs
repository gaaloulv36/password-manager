using System.Security.Cryptography;
using System.Text.Json;
using VaultForge.Core.Crypto;
using VaultForge.Core.Models;
using VaultForge.Core.Storage;

namespace VaultForge.Core.Tests;

/// <summary>Simulates two PCs ("A" and "B") opening copies of the same synced vault file.</summary>
public class VaultMergeTests
{
    private const string Password = "pw";

    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    /// <summary>Round-trips through JSON, exactly like the file going through OneDrive.</summary>
    private static VaultDocument Copy(VaultDocument document) =>
        JsonSerializer.Deserialize<VaultDocument>(JsonSerializer.Serialize(document))!;

    private static (UnlockedVault A, UnlockedVault B) TwoPcs(params VaultItem[] initialItems)
    {
        var a = UnlockedVault.Create(Password, FastKdf());
        foreach (var item in initialItems) a.Upsert(item);
        var b = UnlockedVault.Unlock(Copy(a.Document), Password);
        return (a, b);
    }

    private static string[] Titles(UnlockedVault vault) => vault.GetItems().Select(i => i.Title).Order().ToArray();

    [Fact]
    public void DifferentEntriesAddedOnEachPc_BothAreKept()
    {
        var (a, b) = TwoPcs();
        a.Upsert(new VaultItem { Title = "From A" });
        b.Upsert(new VaultItem { Title = "From B" });

        var result = a.Merge(Copy(b.Document));

        Assert.Equal(["From A", "From B"], Titles(a));
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public void SameEntryEditedOnBothPcs_LatestEditWins()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        a.Upsert(shared with { Title = "Edited on A" });
        b.Upsert(shared with { Title = "Edited on B, later" });

        var result = a.Merge(Copy(b.Document));

        Assert.Equal(["Edited on B, later"], Titles(a));
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public void OlderCopy_DoesNotOverwriteNewerEdits()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        b.Upsert(shared with { Title = "Edited on B" });

        var result = b.Merge(Copy(a.Document));

        Assert.Equal(["Edited on B"], Titles(b));
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void DeletionAfterEdit_Wins()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        a.Upsert(shared with { Title = "Edited on A" });
        b.Delete(shared.Id);

        var result = a.Merge(Copy(b.Document));

        Assert.Empty(a.GetItems());
        Assert.Equal(1, result.Deleted);
    }

    [Fact]
    public void EditAfterDeletion_RestoresEntry()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        a.Delete(shared.Id);
        b.Upsert(shared with { Title = "Edited on B, later" });

        a.Merge(Copy(b.Document));

        Assert.Equal(["Edited on B, later"], Titles(a));
    }

    [Fact]
    public void BothPcs_ConvergeToSameContent()
    {
        var shared = new VaultItem { Title = "Shared" };
        var (a, b) = TwoPcs(shared);
        a.Upsert(new VaultItem { Title = "A1" });
        b.Upsert(shared with { Title = "Shared, edited on B" });
        b.Upsert(new VaultItem { Title = "B1" });
        var aSnapshot = Copy(a.Document);

        a.Merge(Copy(b.Document));
        b.Merge(aSnapshot);

        Assert.Equal(Titles(a), Titles(b));
        Assert.Equal(
            a.Document.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.Revision, i.UpdatedAt)),
            b.Document.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.Revision, i.UpdatedAt)));
    }

    [Fact]
    public void MergingTwice_ChangesNothing()
    {
        var (a, b) = TwoPcs();
        b.Upsert(new VaultItem { Title = "B1" });
        a.Merge(Copy(b.Document));

        Assert.False(a.Merge(Copy(b.Document)).HasChanges);
    }

    [Fact]
    public void MasterPasswordChangedOnOtherPc_IsAdopted()
    {
        var (a, b) = TwoPcs(new VaultItem { Title = "Mail" });
        b.ChangeMasterPassword("new password", FastKdf());

        var result = a.Merge(Copy(b.Document));

        Assert.True(result.MasterPasswordChanged);
        Assert.Equal(["Mail"], Titles(a));
        Assert.Throws<InvalidMasterPasswordException>(() => UnlockedVault.Unlock(Copy(a.Document), Password));
        using var reopened = UnlockedVault.Unlock(Copy(a.Document), "new password");
        Assert.Equal(["Mail"], Titles(reopened));
    }

    [Fact]
    public void EditMadeAfterMerge_IsNeverOlderThanMergedVersion()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        b.Upsert(shared with { Title = "B" });
        a.Merge(Copy(b.Document));
        a.Upsert(shared with { Title = "A, after merge" });

        b.Merge(Copy(a.Document));

        Assert.Equal(["A, after merge"], Titles(b));
    }

    [Fact]
    public void DifferentVault_IsRejected()
    {
        using var a = UnlockedVault.Create(Password, FastKdf());
        using var other = UnlockedVault.Create(Password, FastKdf());

        Assert.Throws<InvalidOperationException>(() => a.Merge(Copy(other.Document)));
    }

    [Fact]
    public void TamperedItemPayload_IsRejected()
    {
        var (a, b) = TwoPcs();
        b.Upsert(new VaultItem { Title = "B1" });
        var remote = Copy(b.Document);
        remote.Items[0].Payload[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => a.Merge(remote));
        Assert.Empty(a.GetItems());
    }

    [Fact]
    public void ForgedTimestamp_IsRejected()
    {
        var shared = new VaultItem { Title = "Original" };
        var (a, b) = TwoPcs(shared);
        a.Upsert(shared with { Title = "Current" });
        var remote = Copy(b.Document);
        remote.Items[0] = remote.Items[0] with { UpdatedAt = DateTimeOffset.UtcNow.AddYears(1) };

        Assert.ThrowsAny<CryptographicException>(() => a.Merge(remote));
        Assert.Equal(["Current"], Titles(a));
    }

    [Fact]
    public void ForgedDeletion_IsRejected()
    {
        var shared = new VaultItem { Title = "Keep me" };
        var (a, b) = TwoPcs(shared);
        var remote = Copy(b.Document);
        remote.Items[0] = remote.Items[0] with { IsDeleted = true, UpdatedAt = DateTimeOffset.UtcNow.AddYears(1) };

        Assert.ThrowsAny<CryptographicException>(() => a.Merge(remote));
        Assert.Equal(["Keep me"], Titles(a));
    }

    [Fact]
    public void ForgedHeader_IsRejected()
    {
        var (a, b) = TwoPcs();
        var remote = Copy(b.Document);
        remote.KeyGeneration = 99;

        Assert.ThrowsAny<CryptographicException>(() => a.Merge(remote));
    }

    [Fact]
    public void Unlock_DetectsTamperedHeader()
    {
        using var a = UnlockedVault.Create(Password, FastKdf());
        var tampered = Copy(a.Document);
        tampered.KeyGeneration = 5;

        Assert.ThrowsAny<CryptographicException>(() => UnlockedVault.Unlock(tampered, Password));
    }
}
