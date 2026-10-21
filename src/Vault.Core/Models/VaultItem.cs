namespace Vault.Core.Models;

/// <summary>Decrypted login entry. Only exists in memory while the vault is unlocked.</summary>
public sealed record VaultItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public List<string> Urls { get; init; } = [];
    public string? Notes { get; init; }
    public string? TotpSecret { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
