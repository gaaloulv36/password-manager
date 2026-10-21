using System.Security.Cryptography;
using Vault.Core.Crypto;
using Vault.Core.Models;

namespace Vault.Core.Tests;

public class QuickUnlockTests
{
    private static KdfParameters FastKdf() => new(RandomNumberGenerator.GetBytes(16), MemoryKiB: 1024, Iterations: 1, Parallelism: 1);

    [Fact]
    public void WrappedKey_UnlocksTheVault()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        vault.Upsert(new VaultItem { Title = "Mail" });
        var deviceSecret = RandomNumberGenerator.GetBytes(256); // e.g. a Windows Hello signature
        var blob = QuickUnlockBlob.Create(vault.Document.VaultId, QuickUnlockBlob.NewChallenge(), deviceSecret, vault.ExportVaultKey());

        using var reopened = UnlockedVault.UnlockWithVaultKey(vault.Document, blob.UnwrapVaultKey(deviceSecret));

        Assert.Equal("Mail", Assert.Single(reopened.GetItems()).Title);
    }

    [Fact]
    public void WrongDeviceSecret_IsRejected()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        var blob = QuickUnlockBlob.Create(vault.Document.VaultId, QuickUnlockBlob.NewChallenge(), RandomNumberGenerator.GetBytes(256), vault.ExportVaultKey());

        Assert.ThrowsAny<CryptographicException>(() => blob.UnwrapVaultKey(RandomNumberGenerator.GetBytes(256)));
    }

    [Fact]
    public void BlobForAnotherVault_IsRejected()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        var secret = RandomNumberGenerator.GetBytes(256);
        var blob = QuickUnlockBlob.Create(vault.Document.VaultId, QuickUnlockBlob.NewChallenge(), secret, vault.ExportVaultKey());

        Assert.ThrowsAny<CryptographicException>(() => (blob with { VaultId = Guid.NewGuid() }).UnwrapVaultKey(secret));
    }

    [Fact]
    public void WrongVaultKey_IsRejectedByHeaderCheck()
    {
        using var vault = UnlockedVault.Create("pw", FastKdf());
        Assert.ThrowsAny<CryptographicException>(() => UnlockedVault.UnlockWithVaultKey(vault.Document, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void StillWorksAfterMasterPasswordChange()
    {
        using var vault = UnlockedVault.Create("old", FastKdf());
        var secret = RandomNumberGenerator.GetBytes(256);
        var blob = QuickUnlockBlob.Create(vault.Document.VaultId, QuickUnlockBlob.NewChallenge(), secret, vault.ExportVaultKey());

        vault.ChangeMasterPassword("new", FastKdf());

        using var reopened = UnlockedVault.UnlockWithVaultKey(vault.Document, blob.UnwrapVaultKey(secret));
        Assert.NotNull(reopened);
    }
}
