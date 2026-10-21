using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using Vault.Core.Crypto;

namespace Vault.Core.Tests;

public class VaultCryptoTests
{
    [Fact]
    public void Argon2id_MatchesRfc9106TestVector()
    {
        // RFC 9106 §5.3
        using var argon = new Argon2id(Enumerable.Repeat((byte)0x01, 32).ToArray())
        {
            Salt = Enumerable.Repeat((byte)0x02, 16).ToArray(),
            KnownSecret = Enumerable.Repeat((byte)0x03, 8).ToArray(),
            AssociatedData = Enumerable.Repeat((byte)0x04, 12).ToArray(),
            MemorySize = 32,
            Iterations = 3,
            DegreeOfParallelism = 4,
        };

        Assert.Equal(
            "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659",
            Convert.ToHexStringLower(argon.GetBytes(32)));
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var key = VaultCrypto.GenerateKey();
        var sealedData = VaultCrypto.Encrypt(key, "secret"u8, "aad"u8);

        Assert.Equal("secret"u8.ToArray(), VaultCrypto.Decrypt(key, sealedData, "aad"u8));
    }

    [Fact]
    public void Encrypt_UsesFreshNonceEachTime()
    {
        var key = VaultCrypto.GenerateKey();
        Assert.NotEqual(VaultCrypto.Encrypt(key, "same"u8, []), VaultCrypto.Encrypt(key, "same"u8, []));
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertext()
    {
        var key = VaultCrypto.GenerateKey();
        var sealedData = VaultCrypto.Encrypt(key, "secret"u8, []);
        sealedData[VaultCrypto.NonceSize] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(key, sealedData, []));
    }

    [Fact]
    public void Decrypt_RejectsWrongAssociatedData()
    {
        var key = VaultCrypto.GenerateKey();
        var sealedData = VaultCrypto.Encrypt(key, "secret"u8, "item-a"u8);

        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(key, sealedData, "item-b"u8));
    }
}
