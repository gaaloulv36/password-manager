using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vault.Core.Crypto;
using Vault.Core.Models;
using Vault.Core.Storage;

namespace Vault.Core;

public sealed class InvalidMasterPasswordException() : Exception("The master password is incorrect.");

/// <summary>An open vault session. Holds the vault key in memory until disposed (i.e. locked).</summary>
public sealed class UnlockedVault : IDisposable
{
    private static readonly byte[] VaultKeyAad = "pm:vault-key:v1"u8.ToArray();

    private readonly byte[] _vaultKey;
    private bool _disposed;

    public VaultDocument Document { get; }

    private UnlockedVault(VaultDocument document, byte[] vaultKey)
    {
        Document = document;
        _vaultKey = vaultKey;
    }

    public static UnlockedVault Create(ReadOnlySpan<char> masterPassword, KdfParameters? kdf = null)
    {
        kdf ??= KdfParameters.CreateDefault();
        var vaultKey = VaultCrypto.GenerateKey();
        var document = new VaultDocument { Kdf = kdf, WrappedVaultKey = WrapKey(vaultKey, masterPassword, kdf) };
        return new UnlockedVault(document, vaultKey);
    }

    public static UnlockedVault Unlock(VaultDocument document, ReadOnlySpan<char> masterPassword)
    {
        var wrappingKey = DeriveWrappingKey(masterPassword, document.Kdf);
        try
        {
            return new UnlockedVault(document, VaultCrypto.Decrypt(wrappingKey, document.WrappedVaultKey, VaultKeyAad));
        }
        catch (CryptographicException)
        {
            throw new InvalidMasterPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Re-wraps the vault key only; items don't need to be re-encrypted.</summary>
    public void ChangeMasterPassword(ReadOnlySpan<char> newMasterPassword, KdfParameters? kdf = null)
    {
        ThrowIfLocked();
        kdf ??= KdfParameters.CreateDefault();
        Document.WrappedVaultKey = WrapKey(_vaultKey, newMasterPassword, kdf);
        Document.Kdf = kdf;
    }

    public IReadOnlyList<VaultItem> GetItems()
    {
        ThrowIfLocked();
        return Document.Items.Where(i => !i.IsDeleted).Select(DecryptItem).ToList();
    }

    public void Upsert(VaultItem item)
    {
        ThrowIfLocked();
        item = item with { UpdatedAt = DateTimeOffset.UtcNow };
        var payload = JsonSerializer.SerializeToUtf8Bytes(item);
        var sealedPayload = VaultCrypto.Encrypt(_vaultKey, payload, ItemAad(item.Id));
        CryptographicOperations.ZeroMemory(payload);

        ReplaceItem(item.Id, revision => new EncryptedItem(item.Id, revision, item.UpdatedAt, sealedPayload));
    }

    /// <summary>Leaves a tombstone so deletions survive merging with an older copy of the file.</summary>
    public void Delete(Guid id)
    {
        ThrowIfLocked();
        ReplaceItem(id, revision => new EncryptedItem(id, revision, DateTimeOffset.UtcNow, Payload: null));
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_vaultKey);
        _disposed = true;
    }

    private void ReplaceItem(Guid id, Func<long, EncryptedItem> create)
    {
        var index = Document.Items.FindIndex(i => i.Id == id);
        var nextRevision = index >= 0 ? Document.Items[index].Revision + 1 : 1;
        var updated = create(nextRevision);

        if (index >= 0) Document.Items[index] = updated;
        else Document.Items.Add(updated);
    }

    private VaultItem DecryptItem(EncryptedItem encrypted)
    {
        var plaintext = VaultCrypto.Decrypt(_vaultKey, encrypted.Payload!, ItemAad(encrypted.Id));
        try
        {
            return JsonSerializer.Deserialize<VaultItem>(plaintext)
                ?? throw new InvalidDataException($"Item {encrypted.Id} is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Binds each ciphertext to its item id, so encrypted payloads can't be swapped between items.</summary>
    private static byte[] ItemAad(Guid id) => Encoding.UTF8.GetBytes($"pm:item:v1:{id:N}");

    private static byte[] WrapKey(byte[] vaultKey, ReadOnlySpan<char> masterPassword, KdfParameters kdf)
    {
        var wrappingKey = DeriveWrappingKey(masterPassword, kdf);
        try
        {
            return VaultCrypto.Encrypt(wrappingKey, vaultKey, VaultKeyAad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static byte[] DeriveWrappingKey(ReadOnlySpan<char> masterPassword, KdfParameters kdf)
    {
        var masterKey = VaultCrypto.DeriveMasterKey(masterPassword, kdf);
        try
        {
            return VaultCrypto.DeriveSubkey(masterKey, "pm:wrap:v1");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private void ThrowIfLocked() => ObjectDisposedException.ThrowIf(_disposed, this);
}
