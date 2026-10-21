using Vault.Core.Crypto;

namespace Vault.Core.Storage;

/// <summary>
/// On-disk shape of the vault. Everything sensitive is encrypted; the rest is metadata needed for
/// unlocking and for merging copies of the file edited on different machines. All metadata is
/// authenticated: the header by <see cref="HeaderMac"/>, each item through its AEAD associated data.
/// </summary>
public sealed class VaultDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Identifies the vault across copies, so unrelated vault files are never merged.</summary>
    public required Guid VaultId { get; init; }

    /// <summary>Incremented on every master password change; the higher generation wins when merging.</summary>
    public long KeyGeneration { get; set; } = 1;

    public required KdfParameters Kdf { get; set; }

    /// <summary>The random vault key, encrypted with a key derived from the master password.</summary>
    public required byte[] WrappedVaultKey { get; set; }

    /// <summary>HMAC over the header fields, keyed from the vault key.</summary>
    public required byte[] HeaderMac { get; set; }

    public List<EncryptedItem> Items { get; init; } = [];
}

/// <summary>
/// One encrypted entry. A deleted entry is kept as a tombstone with an encrypted empty payload,
/// so deletions are authenticated and still reach other copies of the vault.
/// </summary>
public sealed record EncryptedItem(Guid Id, long Revision, DateTimeOffset UpdatedAt, bool IsDeleted, byte[] Payload);
