using Vault.Core;
using Vault.Core.Storage;

namespace Vault.Desktop.Services;

/// <summary>The currently open vault file and, while unlocked, its decrypted session.</summary>
public sealed class VaultSession
{
    public string? Path { get; private set; }
    public UnlockedVault? Vault { get; private set; }
    public bool IsUnlocked => Vault is not null;

    public event EventHandler? Locked;

    /// <summary>Slow (Argon2id) — call off the UI thread.</summary>
    public void Create(string path, string masterPassword)
    {
        var vault = UnlockedVault.Create(masterPassword);
        VaultFileStore.Save(vault.Document, path);
        Open(path, vault);
    }

    /// <summary>Slow (Argon2id) — call off the UI thread.</summary>
    public void Unlock(string path, string masterPassword) =>
        Open(path, UnlockedVault.Unlock(VaultFileStore.Load(path), masterPassword));

    public MergeResult? Save() =>
        VaultFileStore.SaveMerged(Vault ?? throw new InvalidOperationException("The vault is locked."), Path!);

    public void Lock()
    {
        if (Vault is null) return;
        Vault.Dispose();
        Vault = null;
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private void Open(string path, UnlockedVault vault)
    {
        Vault?.Dispose();
        Path = path;
        Vault = vault;
    }
}
