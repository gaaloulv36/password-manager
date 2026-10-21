using Vault.Core.Crypto;

namespace Vault.Core.Storage;

/// <summary>
/// On-disk shape of the vault. Everything sensitive is encrypted; the rest is metadata needed for
/// unlocking and for merging copies of the file edited on different machines.
/// </summary>
public sealed class VaultDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required KdfParameters Kdf { get; set; }

    /// <summary>The random vault key, encrypted with a key derived from the master password.</summary>
    public required byte[] WrappedVaultKey { get; set; }

    public List<EncryptedItem> Items { get; init; } = [];
}

/// <summary>One encrypted entry. <see cref="Payload"/> is null for a deletion tombstone.</summary>
public sealed record EncryptedItem(Guid Id, long Revision, DateTimeOffset UpdatedAt, byte[]? Payload)
{
    public bool IsDeleted => Payload is null;
}
