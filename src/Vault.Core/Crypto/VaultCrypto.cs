using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Vault.Core.Crypto;

/// <summary>
/// All cryptographic primitives used by the vault. Nothing else in the codebase should touch raw crypto APIs.
/// Ciphertext layout: nonce (12) | ciphertext | tag (16), AES-256-GCM.
/// </summary>
public static class VaultCrypto
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static byte[] DeriveMasterKey(ReadOnlySpan<char> password, KdfParameters kdf)
    {
        var passwordBytes = new byte[Encoding.UTF8.GetByteCount(password)];
        try
        {
            Encoding.UTF8.GetBytes(password, passwordBytes);
            using var argon = new Argon2id(passwordBytes)
            {
                Salt = kdf.Salt,
                MemorySize = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                DegreeOfParallelism = kdf.Parallelism,
            };
            return argon.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Derives an independent key for a given purpose (e.g. "enc", "auth") from the master key.</summary>
    public static byte[] DeriveSubkey(ReadOnlySpan<byte> masterKey, string purpose) =>
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey.ToArray(), KeySize, Encoding.UTF8.GetBytes(purpose));

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySize);

    public static byte[] ComputeMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data) => HMACSHA256.HashData(key, data);

    public static bool VerifyMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, ReadOnlySpan<byte> expectedMac) =>
        CryptographicOperations.FixedTimeEquals(ComputeMac(key, data), expectedMac);

    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize, plaintext.Length), output.AsSpan(NonceSize + plaintext.Length), associatedData);
        return output;
    }

    /// <exception cref="CryptographicException">Wrong key, or data was tampered with.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        if (sealedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is too short.");

        var ciphertextLength = sealedData.Length - NonceSize - TagSize;
        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            sealedData[..NonceSize],
            sealedData.Slice(NonceSize, ciphertextLength),
            sealedData[(NonceSize + ciphertextLength)..],
            plaintext,
            associatedData);
        return plaintext;
    }
}
