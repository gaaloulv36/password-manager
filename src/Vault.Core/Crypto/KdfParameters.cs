using System.Security.Cryptography;

namespace Vault.Core.Crypto;

/// <summary>Argon2id parameters. Stored in the vault file so they can be raised later without breaking old vaults.</summary>
public sealed record KdfParameters(byte[] Salt, int MemoryKiB, int Iterations, int Parallelism)
{
    public const int SaltSize = 16;

    /// <summary>256 MiB, 3 passes, 4 lanes — roughly 0.5–1s on a modern desktop.</summary>
    public static KdfParameters CreateDefault() =>
        new(RandomNumberGenerator.GetBytes(SaltSize), MemoryKiB: 256 * 1024, Iterations: 3, Parallelism: 4);

    /// <summary>
    /// Rejects values a real vault would never have. These come from the (untrusted) file and run before
    /// anything can be authenticated, so without limits a crafted file could exhaust memory or hang the app.
    /// </summary>
    /// <exception cref="InvalidDataException"></exception>
    public void Validate()
    {
        if (Salt is not { Length: >= 16 and <= 64 }) throw new InvalidDataException("Invalid KDF salt.");
        if (MemoryKiB is < 1024 or > 4 * 1024 * 1024) throw new InvalidDataException("Invalid KDF memory size.");
        if (Iterations is < 1 or > 64) throw new InvalidDataException("Invalid KDF iteration count.");
        if (Parallelism is < 1 or > 64) throw new InvalidDataException("Invalid KDF parallelism.");
    }
}
