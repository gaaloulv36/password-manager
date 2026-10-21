using System.Security.Cryptography;

namespace Vault.Core.Crypto;

/// <summary>Argon2id parameters. Stored in the vault file so they can be raised later without breaking old vaults.</summary>
public sealed record KdfParameters(byte[] Salt, int MemoryKiB, int Iterations, int Parallelism)
{
    public const int SaltSize = 16;

    /// <summary>256 MiB, 3 passes, 4 lanes — roughly 0.5–1s on a modern desktop.</summary>
    public static KdfParameters CreateDefault() =>
        new(RandomNumberGenerator.GetBytes(SaltSize), MemoryKiB: 256 * 1024, Iterations: 3, Parallelism: 4);
}
