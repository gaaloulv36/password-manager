using System.Security.Cryptography;

namespace Vault.Core.Crypto;

/// <summary>
/// The vault key, wrapped with a secret produced by a device authenticator (Windows Hello: a TPM-held key
/// signs <see cref="Challenge"/> after PIN/biometric). Kept only on this PC, never in the synced vault file.
/// </summary>
public sealed record QuickUnlockBlob(Guid VaultId, byte[] Challenge, byte[] WrappedVaultKey)
{
    private static readonly byte[] Info = "pm:quick-unlock:v1"u8.ToArray();

    public static byte[] NewChallenge() => RandomNumberGenerator.GetBytes(32);

    public static QuickUnlockBlob Create(Guid vaultId, byte[] challenge, ReadOnlySpan<byte> deviceSecret, ReadOnlySpan<byte> vaultKey)
    {
        var key = DeriveKey(deviceSecret, challenge);
        try
        {
            return new QuickUnlockBlob(vaultId, challenge, VaultCrypto.Encrypt(key, vaultKey, vaultId.ToByteArray()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <exception cref="CryptographicException">Wrong device secret (e.g. the Hello key was recreated) or tampered blob.</exception>
    public byte[] UnwrapVaultKey(ReadOnlySpan<byte> deviceSecret)
    {
        var key = DeriveKey(deviceSecret, Challenge);
        try
        {
            return VaultCrypto.Decrypt(key, WrappedVaultKey, VaultId.ToByteArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> deviceSecret, byte[] challenge)
    {
        var key = new byte[VaultCrypto.KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, deviceSecret, key, challenge, Info);
        return key;
    }
}
