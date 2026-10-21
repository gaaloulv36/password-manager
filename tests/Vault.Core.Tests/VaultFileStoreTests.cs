using System.Security.Cryptography;
using Vault.Core.Crypto;
using Vault.Core.Generators;
using Vault.Core.Models;
using Vault.Core.Storage;

namespace Vault.Core.Tests;

public sealed class VaultFileStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pmvault");

    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".bak", _path + ".tmp" })
            File.Delete(file);
    }

    [Fact]
    public void SaveMerged_KeepsChangesWrittenByAnotherPc()
    {
        using var a = UnlockedVault.Create("pw", FastKdf());
        VaultFileStore.Save(a.Document, _path);

        using (var b = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw"))
        {
            b.Upsert(new VaultItem { Title = "Saved on B" });
            VaultFileStore.SaveMerged(b, _path);
        }

        a.Upsert(new VaultItem { Title = "Saved on A" });
        var result = VaultFileStore.SaveMerged(a, _path);

        Assert.Equal(1, result?.Added);
        using var reopened = UnlockedVault.Unlock(VaultFileStore.Load(_path), "pw");
        Assert.Equal(["Saved on A", "Saved on B"], reopened.GetItems().Select(i => i.Title).Order());
    }

    [Fact]
    public void SaveMerged_ToNewPath_ReturnsNull()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        Assert.Null(VaultFileStore.SaveMerged(vault, _path));
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_OverExistingFile_KeepsBackup()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        VaultFileStore.Save(vault.Document, _path);
        vault.Upsert(new VaultItem { Title = "x" });
        VaultFileStore.Save(vault.Document, _path);

        Assert.Empty(VaultFileStore.Load(_path + ".bak").Items);
        Assert.Single(VaultFileStore.Load(_path).Items);
    }
}

public class PasswordStrengthTests
{
    [Theory]
    [InlineData("", StrengthLevel.VeryWeak)]
    [InlineData("aaaaaaaaaaaaaaaa", StrengthLevel.VeryWeak)]
    [InlineData("hello", StrengthLevel.VeryWeak)]
    [InlineData("hunter22", StrengthLevel.Weak)]
    [InlineData("Tr0ub4dor&3", StrengthLevel.Strong)]
    [InlineData("correct-horse-battery-staple", StrengthLevel.VeryStrong)]
    public void Evaluate_RanksPasswords(string password, StrengthLevel expected) =>
        Assert.Equal(expected, PasswordStrength.Evaluate(password));
}
