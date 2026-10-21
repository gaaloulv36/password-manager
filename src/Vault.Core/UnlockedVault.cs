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

    public VaultDocument Document { get; private set; }

    private UnlockedVault(VaultDocument document, byte[] vaultKey)
    {
        Document = document;
        _vaultKey = vaultKey;
    }

    public static UnlockedVault Create(ReadOnlySpan<char> masterPassword, KdfParameters? kdf = null)
    {
        kdf ??= KdfParameters.CreateDefault();
        var vaultKey = VaultCrypto.GenerateKey();
        var document = new VaultDocument
        {
            VaultId = Guid.NewGuid(),
            Kdf = kdf,
            WrappedVaultKey = WrapKey(vaultKey, masterPassword, kdf),
            HeaderMac = [],
        };
        document.HeaderMac = ComputeHeaderMac(vaultKey, document);
        return new UnlockedVault(document, vaultKey);
    }

    /// <exception cref="InvalidMasterPasswordException">Wrong master password.</exception>
    /// <exception cref="CryptographicException">The file header was tampered with.</exception>
    public static UnlockedVault Unlock(VaultDocument document, ReadOnlySpan<char> masterPassword)
    {
        var wrappingKey = DeriveWrappingKey(masterPassword, document.Kdf);
        byte[] vaultKey;
        try
        {
            vaultKey = VaultCrypto.Decrypt(wrappingKey, document.WrappedVaultKey, VaultKeyAad);
        }
        catch (CryptographicException)
        {
            throw new InvalidMasterPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        var vault = new UnlockedVault(document, vaultKey);
        try
        {
            vault.VerifyHeader(document);
            return vault;
        }
        catch
        {
            vault.Dispose();
            throw;
        }
    }

    /// <summary>Re-wraps the vault key only; items don't need to be re-encrypted.</summary>
    public void ChangeMasterPassword(ReadOnlySpan<char> newMasterPassword, KdfParameters? kdf = null)
    {
        ThrowIfLocked();
        kdf ??= KdfParameters.CreateDefault();
        Document.WrappedVaultKey = WrapKey(_vaultKey, newMasterPassword, kdf);
        Document.Kdf = kdf;
        Document.KeyGeneration++;
        Document.HeaderMac = ComputeHeaderMac(_vaultKey, Document);
    }

    public IReadOnlyList<VaultItem> GetItems()
    {
        ThrowIfLocked();
        return Document.Items.Where(i => !i.IsDeleted).Select(DecryptItem).ToList();
    }

    public void Upsert(VaultItem item)
    {
        ThrowIfLocked();
        var (revision, updatedAt) = NextVersion(item.Id);
        var payload = JsonSerializer.SerializeToUtf8Bytes(item with { UpdatedAt = updatedAt });
        try
        {
            Store(Seal(item.Id, revision, updatedAt, isDeleted: false, payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    /// <summary>Leaves a tombstone so the deletion also reaches other copies of the vault.</summary>
    public void Delete(Guid id)
    {
        ThrowIfLocked();
        var (revision, updatedAt) = NextVersion(id);
        Store(Seal(id, revision, updatedAt, isDeleted: true, []));
    }

    /// <summary>
    /// Merges another copy of this vault (e.g. the file as changed on another PC) into this session.
    /// Everything taken from <paramref name="remote"/> is authenticated first, so a tampered file
    /// can't inject, roll back or delete entries.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="remote"/> is a different vault.</exception>
    /// <exception cref="CryptographicException"><paramref name="remote"/> was tampered with.</exception>
    public MergeResult Merge(VaultDocument remote)
    {
        ThrowIfLocked();
        if (remote.VaultId != Document.VaultId)
            throw new InvalidOperationException("This file belongs to a different vault.");

        VerifyHeader(remote);

        var ours = Document.Items.ToDictionary(i => i.Id);
        foreach (var theirs in remote.Items)
        {
            if (!ours.TryGetValue(theirs.Id, out var local) || VaultMerger.CompareItems(theirs, local) > 0)
                CryptographicOperations.ZeroMemory(Open(theirs));
        }

        var result = VaultMerger.Merge(Document, remote);
        Document = result.Document;
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_vaultKey);
        _disposed = true;
    }

    /// <summary>
    /// Next revision and timestamp for an item. The timestamp never goes backwards, even if this PC's
    /// clock is behind the one that made the previous edit — otherwise the new edit would lose a merge.
    /// </summary>
    private (long Revision, DateTimeOffset UpdatedAt) NextVersion(Guid id)
    {
        var existing = Document.Items.Find(i => i.Id == id);
        var now = DateTimeOffset.UtcNow;
        if (existing is null) return (1, now);
        return (existing.Revision + 1, now > existing.UpdatedAt ? now : existing.UpdatedAt.AddTicks(1));
    }

    private void Store(EncryptedItem item)
    {
        var index = Document.Items.FindIndex(i => i.Id == item.Id);
        if (index >= 0) Document.Items[index] = item;
        else Document.Items.Add(item);
    }

    private EncryptedItem Seal(Guid id, long revision, DateTimeOffset updatedAt, bool isDeleted, ReadOnlySpan<byte> payload) =>
        new(id, revision, updatedAt, isDeleted, VaultCrypto.Encrypt(_vaultKey, payload, ItemAad(id, revision, updatedAt, isDeleted)));

    private byte[] Open(EncryptedItem item) =>
        VaultCrypto.Decrypt(_vaultKey, item.Payload, ItemAad(item.Id, item.Revision, item.UpdatedAt, item.IsDeleted));

    private VaultItem DecryptItem(EncryptedItem encrypted)
    {
        var plaintext = Open(encrypted);
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

    /// <summary>
    /// Binds each ciphertext to its id and metadata, so payloads can't be swapped between items and
    /// timestamps/deletion flags can't be forged to win a merge.
    /// </summary>
    private static byte[] ItemAad(Guid id, long revision, DateTimeOffset updatedAt, bool isDeleted) =>
        Encoding.UTF8.GetBytes($"pm:item:v1:{id:N}:{revision}:{updatedAt.UtcTicks}:{(isDeleted ? 1 : 0)}");

    private void VerifyHeader(VaultDocument document)
    {
        var macKey = VaultCrypto.DeriveSubkey(_vaultKey, "pm:header-mac:v1");
        try
        {
            if (!VaultCrypto.VerifyMac(macKey, SerializeHeader(document), document.HeaderMac))
                throw new CryptographicException("The vault header has been modified.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static byte[] ComputeHeaderMac(byte[] vaultKey, VaultDocument document)
    {
        var macKey = VaultCrypto.DeriveSubkey(vaultKey, "pm:header-mac:v1");
        try
        {
            return VaultCrypto.ComputeMac(macKey, SerializeHeader(document));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static byte[] SerializeHeader(VaultDocument document)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(document.FormatVersion);
        writer.Write(document.VaultId.ToByteArray());
        writer.Write(document.KeyGeneration);
        writer.Write(document.Kdf.Salt.Length);
        writer.Write(document.Kdf.Salt);
        writer.Write(document.Kdf.MemoryKiB);
        writer.Write(document.Kdf.Iterations);
        writer.Write(document.Kdf.Parallelism);
        writer.Write(document.WrappedVaultKey.Length);
        writer.Write(document.WrappedVaultKey);
        writer.Flush();
        return stream.ToArray();
    }

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
