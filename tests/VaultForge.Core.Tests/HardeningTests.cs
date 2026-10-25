using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using VaultForge.Core.Crypto;
using VaultForge.Core.Models;
using VaultForge.Core.Storage;

namespace VaultForge.Core.Tests;

/// <summary>Regression tests for the security review findings (see SECURITY.md).</summary>
public sealed class HardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-hardening-{Guid.NewGuid():N}");
    private readonly string _path;

    public HardeningTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "Passwords.pmvault");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    private UnlockedVault CreateSavedVault(params string[] titles)
    {
        var vault = UnlockedVault.Create("pw", FastKdf());
        foreach (var title in titles) vault.Upsert(new VaultItem { Title = title });
        VaultFileStore.Save(vault.Document, _path);
        return vault;
    }

    private void EditFileJson(Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
        edit(json);
        File.WriteAllText(_path, json.ToJsonString());
    }

    // F1: one tampered entry must fail the whole unlock, not leave a half-unlocked session.
    [Fact]
    public void TamperedEntry_FailsUnlock()
    {
        using (CreateSavedVault("A", "B")) { }
        EditFileJson(json =>
        {
            var payload = Convert.FromBase64String((string)json["Items"]![1]!["Payload"]!);
            payload[^1] ^= 1;
            json["Items"]![1]!["Payload"] = Convert.ToBase64String(payload);
        });

        Assert.ThrowsAny<CryptographicException>(() => UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw"));
    }

    [Fact]
    public void TamperedEntry_FailsQuickUnlockToo()
    {
        byte[] key;
        using (var vault = CreateSavedVault("A")) key = vault.ExportVaultKey();
        EditFileJson(json => json["Items"]![0]!["Revision"] = 99);

        Assert.ThrowsAny<CryptographicException>(() => UnlockedVault.UnlockWithVaultKey(VaultFileStore.Load(_path), key));
    }

    // F4: values from the file are checked before Argon2 runs on them.
    [Theory]
    [InlineData("MemoryKiB", 1_000_000_000)]
    [InlineData("MemoryKiB", 0)]
    [InlineData("Iterations", 100_000)]
    [InlineData("Parallelism", 0)]
    public void ImplausibleKdfSettings_AreRejectedOnLoad(string field, int value)
    {
        using (CreateSavedVault()) { }
        EditFileJson(json => json["Kdf"]![field] = value);

        Assert.Throws<InvalidDataException>(() => VaultFileStore.Load(_path));
    }

    [Fact]
    public void DuplicateEntryIds_AreRejectedOnLoad()
    {
        using (CreateSavedVault("A")) { }
        EditFileJson(json => json["Items"]!.AsArray().Add(json["Items"]![0]!.DeepClone()));

        Assert.Throws<InvalidDataException>(() => VaultFileStore.Load(_path));
    }

    [Fact]
    public void MissingPayload_IsRejectedOnLoad()
    {
        using (CreateSavedVault("A")) { }
        EditFileJson(json => json["Items"]![0]!["Payload"] = null);

        Assert.Throws<InvalidDataException>(() => VaultFileStore.Load(_path));
    }

    // F5: a conflict copy that claims to be this vault but doesn't authenticate is set aside; syncing carries on.
    [Fact]
    public void TamperedConflictCopy_IsSetAside_AndSyncContinues()
    {
        using var vault = CreateSavedVault("A");
        var copy = Path.Combine(_dir, "Passwords-LAPTOP.pmvault");
        File.Copy(_path, copy);
        var json = JsonNode.Parse(File.ReadAllText(copy))!.AsObject();
        json["KeyGeneration"] = 50; // header no longer matches its MAC
        File.WriteAllText(copy, json.ToJsonString());

        vault.Upsert(new VaultItem { Title = "B" });
        var archive = Path.Combine(_dir, "archive");
        var result = VaultSync.SyncWithDisk(vault, _path, archive, forceWrite: true);

        Assert.Empty(result.MergedConflictCopies);
        Assert.False(File.Exists(copy));
        Assert.Single(Directory.GetFiles(Path.Combine(archive, "Rejected")));
        using var reopened = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw");
        Assert.Equal(["A", "B"], reopened.GetItems().Select(i => i.Title).Order());
    }
}
