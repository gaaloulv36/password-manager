using System.Security.Cryptography;
using VaultForge.Core.Crypto;
using VaultForge.Core.Models;
using VaultForge.Core.Storage;

namespace VaultForge.Core.Tests;

/// <summary>Two PCs ("A" and "B") sharing one vault file in a synced folder.</summary>
public sealed class VaultSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-sync-{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly string _archive;
    private readonly UnlockedVault _a;
    private readonly UnlockedVault _b;

    public VaultSyncTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "Passwords.pmvault");
        _archive = Path.Combine(_dir, "archive");

        _a = UnlockedVault.Create("pw", new KdfParameters(RandomNumberGenerator.GetBytes(16), 1024, 1, 1));
        VaultFileStore.Save(_a.Document, _path);
        _b = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw");
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static string[] Titles(UnlockedVault vault) => vault.GetItems().Select(i => i.Title).Order().ToArray();

    [Fact]
    public void ChangesSavedByAnotherPc_ArePulledIn()
    {
        _b.Upsert(new VaultItem { Title = "From B" });
        VaultSync.SyncWithDisk(_b, _path, _archive, forceWrite: true);

        var result = VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: false);

        Assert.Equal(1, result.Added);
        Assert.Equal(["From B"], Titles(_a));
    }

    [Fact]
    public void NothingNew_DoesNotRewriteTheFile()
    {
        _b.Upsert(new VaultItem { Title = "From B" });
        VaultSync.SyncWithDisk(_b, _path, _archive, forceWrite: true);
        VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: false);
        var before = File.GetLastWriteTimeUtc(_path);
        Thread.Sleep(20);

        var again = VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: false);

        Assert.False(again.HasChanges);
        Assert.Equal(before, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void LocalChangesMissingFromFile_AreWrittenBack()
    {
        _a.Upsert(new VaultItem { Title = "From A" });

        VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: false);

        using var reopened = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw");
        Assert.Equal(["From A"], Titles(reopened));
    }

    [Fact]
    public void ConflictCopy_IsMergedAndArchived()
    {
        // B saved while offline; OneDrive kept both versions.
        var copy = Path.Combine(_dir, "Passwords-DESKTOP-B42.pmvault");
        _b.Upsert(new VaultItem { Title = "From B (conflict)" });
        VaultFileStore.Save(_b.Document, copy);
        _a.Upsert(new VaultItem { Title = "From A" });

        var result = VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: true);

        Assert.Equal(["From A", "From B (conflict)"], Titles(_a));
        Assert.Equal([copy], result.MergedConflictCopies);
        Assert.False(File.Exists(copy));
        Assert.Single(Directory.GetFiles(_archive));
        using var reopened = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw");
        Assert.Equal(["From A", "From B (conflict)"], Titles(reopened));
    }

    [Fact]
    public void CopyOfAnotherVault_IsLeftAlone()
    {
        using var other = UnlockedVault.Create("pw", new KdfParameters(RandomNumberGenerator.GetBytes(16), 1024, 1, 1));
        var otherPath = Path.Combine(_dir, "Passwords-OTHERPC.pmvault");
        VaultFileStore.Save(other.Document, otherPath);

        var result = VaultSync.SyncWithDisk(_a, _path, _archive, forceWrite: false);

        Assert.Empty(result.MergedConflictCopies);
        Assert.True(File.Exists(otherPath));
    }

    [Theory]
    [InlineData("Passwords-DESKTOP-AB12.pmvault", true)]
    [InlineData("Passwords-LAPTOP.pmvault", true)]
    [InlineData("Passwords (Bob's conflicted copy 2026-10-21).pmvault", true)]
    [InlineData("Passwords.sync-conflict-20261021-101500-ABCDEFG.pmvault", true)]
    [InlineData("Passwords (1).pmvault", true)]
    [InlineData("Passwords backup 2026-10-21.pmvault", false)]
    [InlineData("Work.pmvault", false)]
    [InlineData("PasswordsOld.pmvault", false)]
    [InlineData("Passwords-DESKTOP.txt", false)]
    public void FindConflictCopies_RecognisesSyncServiceNames(string fileName, bool expected)
    {
        File.WriteAllText(Path.Combine(_dir, fileName), "x");
        Assert.Equal(expected, VaultSync.FindConflictCopies(_path).Any(f => Path.GetFileName(f) == fileName));
    }
}
