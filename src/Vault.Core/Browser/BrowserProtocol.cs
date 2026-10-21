using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Vault.Core.Crypto;

namespace Vault.Core.Browser;

/// <summary>
/// Shared constants for the browser extension ⇄ native host ⇄ desktop app link.
/// The extension's src/protocol.js must stay byte-for-byte compatible with this file.
/// </summary>
public static class BrowserProtocol
{
    public const string HostName = "com.passwordmanager.host";
    public const string ToApp = "ext-to-app";
    public const string ToExtension = "app-to-ext";
    public const int PublicKeySize = 65; // uncompressed P-256 point: 0x04 | X | Y

    private static readonly byte[] PairingInfo = "pm-browser-pairing-v1"u8.ToArray();
    private static readonly byte[] CodeLabel = "pm-pairing-code"u8.ToArray();

    /// <summary>Named pipe between native host and app; per Windows user (the pipe is also CurrentUserOnly).</summary>
    public static string PipeName { get; } = "PasswordManager.Browser." + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes($@"{Environment.UserDomainName}\{Environment.UserName}")))[..16];

    /// <summary>
    /// Derives the message key and the 6-digit confirmation code from an ECDH shared secret.
    /// Both public keys are mixed in, so a man-in-the-middle ends up with a different code on each side.
    /// </summary>
    public static (byte[] MessageKey, string Code) DeriveKeys(ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> appPublicKey, ReadOnlySpan<byte> extensionPublicKey)
    {
        var salt = new byte[appPublicKey.Length + extensionPublicKey.Length];
        appPublicKey.CopyTo(salt);
        extensionPublicKey.CopyTo(salt.AsSpan(appPublicKey.Length));

        var okm = new byte[64];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, okm, salt, PairingInfo);
        var messageKey = okm[..32];
        var mac = HMACSHA256.HashData(okm.AsSpan(32), CodeLabel);
        CryptographicOperations.ZeroMemory(okm);

        var value = BinaryPrimitives.ReadUInt32BigEndian(mac) % 1_000_000;
        var digits = value.ToString("D6", CultureInfo.InvariantCulture);
        return (messageKey, $"{digits[..3]} {digits[3..]}");
    }

    /// <summary>Encrypts a message; the direction is bound in so a message can't be reflected back to its sender.</summary>
    public static string Seal(byte[] messageKey, string pairingId, string direction, ReadOnlySpan<byte> plaintext) =>
        Convert.ToBase64String(VaultCrypto.Encrypt(messageKey, plaintext, Aad(pairingId, direction)));

    /// <exception cref="CryptographicException">Wrong key, wrong direction, or tampered.</exception>
    public static byte[] Open(byte[] messageKey, string pairingId, string direction, string sealedBase64) =>
        VaultCrypto.Decrypt(messageKey, Convert.FromBase64String(sealedBase64), Aad(pairingId, direction));

    private static byte[] Aad(string pairingId, string direction) => Encoding.UTF8.GetBytes($"{pairingId}|{direction}");
}

/// <summary>App side of pairing: an ephemeral P-256 key pair.</summary>
public sealed class PairingHandshake : IDisposable
{
    private readonly ECDiffieHellman _ecdh;

    public PairingHandshake() : this(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)) { }

    /// <summary>For tests: a fixed key pair.</summary>
    public PairingHandshake(ECDiffieHellman ecdh) => _ecdh = ecdh;

    public byte[] PublicKey => EncodePoint(_ecdh.ExportParameters(includePrivateParameters: false).Q);

    /// <exception cref="CryptographicException">The extension's public key is not a valid P-256 point.</exception>
    public (byte[] MessageKey, string Code) Complete(byte[] extensionPublicKey)
    {
        if (extensionPublicKey.Length != BrowserProtocol.PublicKeySize || extensionPublicKey[0] != 0x04)
            throw new CryptographicException("Invalid public key.");

        ECDiffieHellman peer;
        try
        {
            peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = extensionPublicKey[1..33], Y = extensionPublicKey[33..] },
            });
        }
        catch (Exception ex) when (ex is not CryptographicException)
        {
            // Windows CNG reports a point that isn't on the curve as PlatformNotSupportedException.
            throw new CryptographicException("Invalid public key.", ex);
        }

        using var _ = peer;
        var shared = _ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        try
        {
            return BrowserProtocol.DeriveKeys(shared, PublicKey, extensionPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public void Dispose() => _ecdh.Dispose();

    private static byte[] EncodePoint(ECPoint q) => [0x04, .. q.X!, .. q.Y!];
}
